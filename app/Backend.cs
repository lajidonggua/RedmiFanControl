using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Management;
using EcResearch;
namespace RedmiFanControl {
    sealed class Hardware : IDisposable {
        StrictSuperIoProbe fan,temp;
        public Hardware(){
            string model=Query("Win32_ComputerSystem","Model"),bios=Query("Win32_BIOS","SMBIOSBIOSVersion"),cpu=Query("Win32_Processor","Name");
            if(model!="Redmi Book Pro 15 2022" || bios!="RMARB5B0P1111" || cpu.IndexOf("6800H",StringComparison.Ordinal)<0)
                throw new InvalidOperationException("仅支持 Redmi Book Pro 15 2022 / 6800H / P.11，设备核验不符。未写入硬件。");
            string modules=AppPaths.Modules;RuntimeModules.Verify(modules);
            fan=new StrictSuperIoProbe(AppPaths.DriverLibrary,Path.Combine(modules,"LpcIO.bin"));
            try{temp=new StrictSuperIoProbe(AppPaths.DriverLibrary,Path.Combine(modules,"AMDFamily17.bin"));}
            catch{fan.Dispose();throw;}
        }
        static string Query(string type,string field){using(var search=new ManagementObjectSearcher("SELECT "+field+" FROM "+type)){foreach(ManagementObject item in search.Get())using(item)return Convert.ToString(item[field]);}return "";}
        public double Temperature(){return temp.ReadCpuTemperature();}
        public StrictSuperIoProbe.ControlState State(){return fan.ReadControlState();}
        public StrictSuperIoProbe.ControlState Set(int percent,int oldRaw){return fan.SetLinkedPercent(percent,oldRaw);}
        public StrictSuperIoProbe.ControlState Auto(Ownership owned){return fan.RestoreAutomaticForOwnedTargets(owned.Targets());}
        public Frame Snapshot(string session,double? sampledTemperature=null){
            double t=sampledTemperature??Temperature();var s=State();
            return new Frame{Session=session,Utc=DateTime.UtcNow.ToString("o"),Connected=true,CpuC=t,Manual=s.Manual,Rpm1=s.Rpm1,Rpm2=s.Rpm2,
                Target1=s.Target1,Target2=s.Target2,Output1=StrictSuperIoProbe.RawToPercent(s.Pwm1),Output2=StrictSuperIoProbe.RawToPercent(s.Pwm2),AutoConfirmed=!s.Manual};
        }
        public void Dispose(){if(temp!=null)temp.Dispose();if(fan!=null)fan.Dispose();}
    }
    static class Backend {
        static string StatePath(string run){return Path.Combine(run,"state.json");}
        static void ValidateRun(string run,string session){
            string prefix=Path.GetFullPath(AppPaths.Runs)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(run).StartsWith(prefix,StringComparison.OrdinalIgnoreCase) || !Directory.Exists(run) || session.Length!=48)
                throw new InvalidOperationException("Invalid local application session.");
            foreach(char c in session)if(!Uri.IsHexDigit(c))throw new InvalidOperationException("Invalid session token.");
        }
        public static int Run(string run,string session,long parent,long parentStart,bool guardian){
            ValidateRun(run,session);
            if(guardian)return Guardian(run,session,parent,parentStart);
            using(var single=new Mutex(false,"Global\\RedmiFanControl.TM2113.ControlOwner")){
                bool locked=false;
                try{try{locked=single.WaitOne(0);}catch(AbandonedMutexException){locked=true;}
                    if(!locked)throw new InvalidOperationException("已有控制后台运行，拒绝重复接管。");
                    return Worker(run,session,parent,parentStart);
                }catch(Exception ex){Files.Write(StatePath(run),new Frame{Session=session,Utc=DateTime.UtcNow.ToString("o"),Ended=true,Error=ex.Message,Status="后台启动失败"});return 1;}
                finally{if(locked)single.ReleaseMutex();}
            }
        }
        static int Worker(string run,string session,long parent,long parentStart){
            string events=Path.Combine(run,"events.jsonl"),ownerPath=Path.Combine(run,"ownership.json");
            Ownership owned=null; Hardware hw=null; Process guard=null; bool fault=false;int ack=0;Frame frame=null;
            string faultText=null; var lease=new Lease();var startup=Stopwatch.StartNew();var output=Stopwatch.StartNew();
            ControlPolicy policy=null;
            var guardianLease=new FrameLease(3);string activeSettings=null;
            try {
                hw=new Hardware();frame=hw.Snapshot(session);Files.Write(StatePath(run),frame);
                using(var current=Process.GetCurrentProcess())guard=Program.Spawn("--guardian",run,session,current.Id,current.StartTime.ToUniversalTime().Ticks,false);
                Files.Event(events,"started",new {guardian_pid=guard.Id,parent=parent});
                while(true){
                    bool uiAlive=Files.Alive(parent,parentStart);
                    bool uiLease=lease.Observe(Files.Read<Message>(Path.Combine(run,"heartbeat.json")),session);
                    if(!uiAlive || (!uiLease && startup.Elapsed.TotalSeconds>8))break;
                    var trip=Files.Read<Frame>(Path.Combine(run,"trip.json"));
                    if(trip!=null && trip.Session==session){fault=true;faultText=trip.Error;}
                    bool guardReady=guardianLease.Observe(Files.Read<Frame>(Path.Combine(run,"guardian.json")),session);
                    if(!guardReady && startup.Elapsed.TotalSeconds>8){fault=true;faultText="独立温度看护中断，已停止手动控制。";}
                    if(fault && owned!=null && owned.Active){Recover(hw,owned,ownerPath,events);}
                    var command=Files.Read<Message>(Path.Combine(run,"command.json"));
                    if(command!=null && command.Session==session && command.Sequence>ack){
                        ack=command.Sequence;
                        try {
                            if(command.Command=="QUIT")break;
                            if(command.Command=="AUTO"){
                                policy=null;activeSettings=null;
                                if(owned!=null && owned.Active)Recover(hw,owned,ownerPath,events);
                                Files.Event(events,"auto_requested",null);
                                if(!fault)faultText=null;
                            }else if(command.Command=="SET" || command.Command=="RPM" || command.Command=="CURVE"){
                                if(fault || !uiLease || !guardReady)throw new InvalidOperationException("安全看护尚未就绪，不能进入手动模式。");
                                if(hw.Temperature()>=ThermalLimits.EmergencyC)throw new InvalidOperationException("CPU 温度达到 95°C，请保持系统自动温控。");
                                var candidate=new ControlPolicy(command,startup.Elapsed.TotalSeconds);
                                var current=hw.Snapshot(session);
                                int percent=candidate.Next(startup.Elapsed.TotalSeconds,current.CpuC.Value,current.Rpm1,current.Rpm2);
                                ApplyPercent(hw,ref owned,session,run,ownerPath,events,percent,candidate.StopBelowC,candidate.MinimumRunningPercent);
                                policy=candidate;activeSettings=ControlPolicy.SettingsKey(command);frame.SetPercent=percent;faultText=null;
                                Files.Event(events,"policy_selected",command);
                            }else throw new InvalidOperationException("Unknown application command.");
                        }catch(Exception ex){
                            policy=null;
                            faultText=ex.Message;Files.Event(events,"command_rejected",ex.Message);
                            if(owned!=null && owned.Active){Recover(hw,owned,ownerPath,events);}
                        }
                    }
                    if(output.ElapsedMilliseconds>=500){
                        try{
                            int percent=frame==null?0:frame.SetPercent;
                            frame=hw.Snapshot(session);frame.SetPercent=percent;
                            if(owned!=null && owned.Active && (!frame.Manual || frame.Target1!=owned.NewRaw || frame.Target2!=owned.NewRaw)){
                                policy=null;
                                owned.Active=false;Files.Write(ownerPath,owned);
                                if(frame.Manual){fault=true;faultText="检测到外部控制器接管，本应用改为只监测。";}
                            }
                            if(frame.CpuC>=ThermalLimits.EmergencyC && owned!=null && owned.Active){
                                policy=null;
                                fault=true;faultText="CPU 温度达到 95°C，已恢复自动；请重新连接后手动应用。";
                                Recover(hw,owned,ownerPath,events);frame=hw.Snapshot(session);
                            }
                            if(policy!=null && owned!=null && owned.Active && !fault){
                                double decisionTemperature=frame.CpuC.Value;
                                int next=policy.Next(startup.Elapsed.TotalSeconds,decisionTemperature,frame.Rpm1,frame.Rpm2);
                                ApplyPercent(hw,ref owned,session,run,ownerPath,events,next,policy.StopBelowC,policy.MinimumRunningPercent);
                                frame=hw.Snapshot(session,decisionTemperature);frame.SetPercent=next;
                                frame.ControlMode=policy.Mode;frame.RequestedRpm=policy.RequestedRpm;
                                frame.SafetyStatus=policy.SafetyStatus;
                                frame.RequestedOutputPercent=policy.RequestedOutputPercent;frame.DecisionReason=policy.DecisionReason;frame.RuleCode=policy.RuleCode;
                            }
                            frame.Owned=owned!=null && owned.Active;
                            frame.ActiveSettingsKey=frame.Owned && policy!=null && !fault?activeSettings:null;
                            frame.CanControl=!fault && guardReady && uiLease && frame.CpuC<ThermalLimits.EmergencyC && (!frame.Manual || frame.Owned);
                            frame.Ack=ack;frame.Error=faultText;
                            frame.Status=fault?"安全保护 · 仅监测":frame.Manual?(frame.Owned?(frame.ControlMode=="RPM"?"目标转速（以较慢一路为准）":frame.ControlMode=="CURVE"?"CPU 温度曲线":"手动百分比 · 两路联动"):"其他程序控制 · 仅监测"):"系统自动温控";
                            Files.Write(StatePath(run),frame);output.Restart();
                        }catch(Exception ex){
                            policy=null;fault=true;faultText="控制保护已停止调速："+ex.Message;
                            Files.Event(events,"sensor_error",ex.Message);
                            if(owned!=null && owned.Active)Recover(hw,owned,ownerPath,events);
                            frame=new Frame{Session=session,Utc=DateTime.UtcNow.ToString("o"),Error=faultText,Status="传感器不可用"};
                            Files.Write(StatePath(run),frame);output.Restart();
                        }
                    }
                    // The guardian uses this timestamp only as a liveness indication, never as telemetry.
                    Files.Write(Path.Combine(run,"worker.json"),new Frame{Session=session,Utc=DateTime.UtcNow.ToString("o"),Connected=true});
                    Thread.Sleep(100);
                }
                return 0;
            }catch(Exception ex){faultText=ex.Message;Files.Event(events,"worker_error",ex.ToString());return 1;}
            finally{
                bool restored=false;
                if(hw!=null){
                    try{if(owned!=null && owned.Active)Recover(hw,owned,ownerPath,events);frame=hw.Snapshot(session);restored=!frame.Manual;}
                    catch(Exception ex){faultText="恢复自动未确认："+ex.Message;Files.TryEvent(events,"recovery_error",ex.ToString());}
                    hw.Dispose();
                }
                if(frame==null)frame=new Frame();
                frame.Session=session;frame.Utc=DateTime.UtcNow.ToString("o");frame.Ended=true;frame.CanControl=false;frame.AutoConfirmed=restored;
                frame.Owned=owned!=null && owned.Active;frame.Error=faultText;frame.Status=restored?"已恢复自动 · 后台停止":"后台停止";
                Files.TryWrite(StatePath(run),frame);
                if(guard!=null)guard.Dispose();
            }
        }
        static void ApplyPercent(Hardware hw,ref Ownership owned,string session,string run,string ownerPath,string events,int percent,int stopBelowC,int minimumRunning){
            int raw=StrictSuperIoProbe.PercentToRaw(percent);
            if(owned!=null && owned.Active && owned.NewRaw==raw){if(owned.StopBelowC!=stopBelowC || owned.MinimumRunningPercent!=minimumRunning){owned.StopBelowC=stopBelowC;owned.MinimumRunningPercent=minimumRunning;Files.Write(ownerPath,owned);}return;}
            int previous=owned!=null && owned.Active?owned.NewRaw:-1;
            owned=new Ownership{Session=session,Active=true,OldRaw=previous,NewRaw=raw,StopBelowC=stopBelowC,MinimumRunningPercent=minimumRunning};
            // Include zero in the recovery ticket: manual-zero is NOT automatic mode.
            Files.Write(ownerPath,owned);
            if(File.Exists(Path.Combine(run,"trip.json")))throw new InvalidOperationException("安全保护已触发。");
            var applied=hw.Set(percent,previous);
            owned.OldRaw=-1;Files.Write(ownerPath,owned);
            Files.Event(events,"manual_applied",new {percent=percent,raw=raw,state=applied});
        }
        static void Recover(Hardware hw,Ownership owned,string path,string events){
            var state=hw.Auto(owned);owned.Active=false;Files.TryWrite(path,owned);Files.TryEvent(events,"automatic_restored",state);
        }
        static int Guardian(string run,string session,long worker,long workerStart){
            string output=Path.Combine(run,"guardian.json"),events=Path.Combine(run,"guardian-events.jsonl");
            var lease=new Lease();var startup=Stopwatch.StartNew(); Hardware hw=null;
            bool tripped=false;string reason=null;Ownership owned=null;
            var lowSpeed=new LowSpeedGuard();
            var workerLease=new FrameLease(4);
            try{
                hw=new Hardware();
                while(true){
                    reason=null;
                    owned=Files.Read<Ownership>(Path.Combine(run,"ownership.json"));
                    if(owned!=null && owned.Session!=session)owned=null;
                    bool alive=Files.Alive(worker,workerStart);
                    bool uiLease=lease.Observe(Files.Read<Message>(Path.Combine(run,"heartbeat.json")),session);
                    var heartbeat=Files.Read<Frame>(Path.Combine(run,"worker.json"));
                    bool workerFresh=workerLease.Observe(heartbeat,session);
                    Frame frame=null;
                    try{frame=hw.Snapshot(session);}
                    catch(Exception ex){reason="独立温度/转速监测失败："+ex.Message;}
                    string lowReason=frame==null?null:lowSpeed.Check(startup.Elapsed.TotalSeconds,owned,frame);
                    if(lowReason!=null)reason=lowReason;
                    if(owned!=null && owned.Active){
                        if(!alive || (!workerFresh && startup.Elapsed.TotalSeconds>6))reason="控制后台失联，已执行独立恢复。";
                        if(!uiLease && startup.Elapsed.TotalSeconds>6)reason="窗口失联，已恢复系统自动温控。";
                        if(frame!=null && frame.CpuC>=ThermalLimits.EmergencyC)reason="CPU 温度达到 95°C，已触发独立恢复。";
                        if(reason!=null){
                            tripped=true;
                            Files.TryWrite(Path.Combine(run,"trip.json"),new Frame{Session=session,Utc=DateTime.UtcNow.ToString("o"),Error=reason});
                            bool recovered=false;
                            for(int attempt=0;attempt<20;attempt++){
                                try{var state=hw.Auto(owned);owned.Active=false;Files.TryWrite(Path.Combine(run,"ownership.json"),owned);Files.TryEvent(events,"guardian_restored",state);recovered=true;break;}
                                catch(Exception ex){Files.TryEvent(events,"recovery_retry",ex.Message);Thread.Sleep(250);}
                            }
                            if(!recovered)throw new InvalidOperationException("自动恢复未确认，请保留日志并立即检查/重启系统。");
                            frame=hw.Snapshot(session);frame.Error=reason;frame.Status="独立保护已恢复自动";
                        }
                    }
                    if(frame==null)frame=new Frame{Session=session,Utc=DateTime.UtcNow.ToString("o"),Error=reason};
                    frame.Ended=!alive;frame.CanControl=false;Files.Write(output,frame);
                    if(!alive)break;
                    if(tripped)reason=null;
                    Thread.Sleep(350);
                }
                return 0;
            }catch(Exception ex){
                bool recovered=false;
                if(hw!=null && owned!=null && owned.Active){try{hw.Auto(owned);owned.Active=false;Files.TryWrite(Path.Combine(run,"ownership.json"),owned);recovered=true;}catch{}}
                Files.TryEvent(events,"guardian_error",ex.ToString());Files.TryWrite(output,new Frame{Session=session,Utc=DateTime.UtcNow.ToString("o"),Ended=true,AutoConfirmed=recovered,Error=ex.Message});return 1;
            }
            finally{if(hw!=null)hw.Dispose();}
        }
    }
}
