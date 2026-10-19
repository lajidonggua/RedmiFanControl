using System;
using System.IO;
using EcResearch;
namespace RedmiFanControl {
    static class AppTests {
        static string testDirectory;
        public static int Run(string directory){
            testDirectory=Path.GetFullPath(directory);Directory.CreateDirectory(testDirectory);
            string output=Path.Combine(testDirectory,"selftest.txt");
            try{
                if(StrictSuperIoProbe.PercentToRaw(50)!=64 || StrictSuperIoProbe.PercentToRaw(60)!=77 || StrictSuperIoProbe.PercentToRaw(99)!=126 || StrictSuperIoProbe.PercentToRaw(100)!=127)throw new Exception("Percentage conversion");
                if(StrictSuperIoProbe.PercentToRaw(0)!=0 || StrictSuperIoProbe.PercentToRaw(1)!=1 || StrictSuperIoProbe.PercentToRaw(49)!=63)throw new Exception("Low range conversion");
                foreach(int bad in new int[]{-1,101,255}){
                    bool rejected=false;try{StrictSuperIoProbe.PercentToRaw(bad);}catch(ArgumentOutOfRangeException){rejected=true;}if(!rejected)throw new Exception("Range guard");
                }
                if(Math.Abs(StrictSuperIoProbe.DecodeCpuTemperature((uint)(480<<21))-60)>0.001)throw new Exception("CPU temperature conversion");
                var lease=new Lease();if(lease.Observe(null,"A"))throw new Exception("Missing heartbeat accepted");
                if(lease.Observe(new Message{Session="B",Sequence=1},"A"))throw new Exception("Foreign heartbeat accepted");
                if(!lease.Observe(new Message{Session="A",Sequence=1},"A"))throw new Exception("Valid heartbeat rejected");
                System.Threading.Thread.Sleep(4100);
                if(lease.Observe(new Message{Session="A",Sequence=1},"A"))throw new Exception("Replayed heartbeat renewed a stale lease");
                if(!lease.Observe(new Message{Session="A",Sequence=2},"A"))throw new Exception("New heartbeat not accepted");
                var own=new Ownership{OldRaw=77,NewRaw=102};if(own.Targets().Length!=2)throw new Exception("Recovery target set");
                own=new Ownership{OldRaw=0,NewRaw=1};if(own.Targets().Length!=2 || own.Targets()[0]!=0)throw new Exception("Zero ownership lost");
                PolicyTests();
                StatelessRegressionTests();
                ConfigurationTests();
                UiPreferencesTests();
                RuntimeAndPresentationTests();
                FrameLeaseTests();
                using(var icon=AppBrand.WindowIcon())Require(icon.Width==32 && icon.Height==32,"Embedded window/tray icon size");
                using(var logo=AppBrand.HeaderImage())Require(logo.Width==256 && logo.Height==256 && logo.GetPixel(0,0).A==0,"Embedded logo size/transparency");
                string path=Path.Combine(testDirectory,"selftest-state.json");
                Files.Write(path,new Frame{Session="test",CpuC=42.5,Rpm1=2642});var f=Files.Read<Frame>(path);
                if(f==null || f.CpuC!=42.5 || f.Rpm1!=2642)throw new Exception("IPC serialization");
                System.Windows.Forms.Application.EnableVisualStyles();System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                using(var ui=new MainForm(true))ui.VerifyUiContract();
                using(var ui=new MainForm(true))ui.VerifyUiStates();
                using(var ui=new MainForm(true))ui.VerifyResizeLayout();
                using(var ui=new MainForm(true))ui.VerifyAutostartUi();
                using(var ui=new MainForm(true))ui.VerifyShutdownContract(testDirectory);
                using(var ui=new MainForm(true))ui.VerifyCloseBehaviorUi();
                var startupStore=new MemoryStartupStore();Autostart.Set(startupStore,@"C:\Fan Control\RedmiFanControl.exe",true);
                Require(startupStore.Read()=="\"C:\\Fan Control\\RedmiFanControl.exe\" --startup","Startup quoting / safe startup flag");
                Autostart.Set(startupStore,@"C:\Fan Control\RedmiFanControl.exe",false);Require(startupStore.Read()==null,"Startup removal");
                Reject(delegate{Autostart.Command("relative.exe");});Reject(delegate{Autostart.Command("C:\\bad\"name.exe");});
                using(var editor=new CurveEditor(new UserConfiguration().Curve))editor.VerifyContract();
                File.WriteAllText(output,"PASS: maximize/restore/resize/scroll layout and all mode editors; remembered close behavior and tray/window state; independent EXE runtime extraction and tamper repair; legacy config migration without overwrite; asynchronous QUIT and same-session recovery; active-mode-only UI; stopped vs unavailable telemetry; thermal sweeps, requested->target rules, guardian floors, stop/stall, persistence/editor/heartbeats and 95C boundary. No hardware or real user settings accessed.");return 0;
            }catch(Exception ex){File.WriteAllText(output,"FAIL: "+ex);return 1;}
        }
        static void Require(bool ok,string text){if(!ok)throw new Exception(text);}
        static void ConfigurationTests(){
            string path=Path.Combine(testDirectory,"selftest-configuration.json");
            var value=new UserConfiguration{Mode=2,AllowFanStop=true,StopBelowC=55,MinimumRunningPercent=25};ConfigurationStore.Save(path,value);
            string note;var loaded=ConfigurationStore.Load(path,out note);
            Require(loaded.Mode==2 && loaded.AllowFanStop && loaded.StopBelowC==55 && loaded.MinimumRunningPercent==25 && loaded.Curve.Length==6,"Settings roundtrip");
            loaded.Percent=80;ConfigurationStore.Save(path,loaded);
            Files.Write(path,new {Version=999});loaded=ConfigurationStore.Load(path,out note);
            Require(loaded.Percent==60 && note.Contains("备份"),"Invalid primary backup recovery");
            value.StopBelowC=95;Reject(delegate{value.Validate();});
        }
        static void UiPreferencesTests(){
            string path=Path.Combine(testDirectory,"selftest-ui-preferences.json");
            var value=new UiPreferences{CloseBehavior=CloseBehavior.HideToTray,LastInTray=true};UiPreferencesStore.Save(path,value);
            string note;var loaded=UiPreferencesStore.Load(path,out note);
            Require(loaded.CloseBehavior==CloseBehavior.HideToTray && loaded.LastInTray && note.Contains("已加载"),"UI preferences roundtrip");
            loaded.CloseBehavior=CloseBehavior.Exit;loaded.LastInTray=false;UiPreferencesStore.Save(path,loaded);
            Files.Write(path,new {Version=999,CloseBehavior=0,LastInTray=true});loaded=UiPreferencesStore.Load(path,out note);
            Require(loaded.CloseBehavior==CloseBehavior.HideToTray && loaded.LastInTray && note.Contains("备份"),"Invalid UI preferences backup recovery");
            loaded.Version=999;Reject(delegate{loaded.Validate();});
            var defaultValue=new UiPreferences();Require(defaultValue.CloseBehavior==CloseBehavior.Ask && !defaultValue.LastInTray,"UI preferences safe defaults");
        }
        static void RuntimeAndPresentationTests(){
            string modules=Path.Combine(testDirectory,"embedded-runtime");RuntimeModules.Prepare(modules);RuntimeModules.Verify(modules);
            File.WriteAllText(Path.Combine(modules,"LpcIO.bin"),"damaged");Reject(delegate{RuntimeModules.Verify(modules);});
            RuntimeModules.Prepare(modules);RuntimeModules.Verify(modules);
            string legacy=Path.Combine(testDirectory,"legacy.json"),destination=Path.Combine(testDirectory,"migrated.json");
            ConfigurationStore.Save(legacy,new UserConfiguration{Percent=30});string note;
            var migrated=ConfigurationStore.LoadWithMigration(destination,legacy,out note);
            Require(migrated.Percent==30 && File.Exists(destination) && note.Contains("迁移"),"Legacy configuration migration");
            ConfigurationStore.Save(destination,new UserConfiguration{Percent=40});
            Require(ConfigurationStore.LoadWithMigration(destination,legacy,out note).Percent==40,"Migration must not overwrite new settings");
            var live=new Frame{Utc=DateTime.UtcNow.ToString("o"),Connected=true,CanControl=true,CpuC=38};
            Require(Presentation.Describe(live,null,false,0).Detail.Contains("0 RPM 是有效读数"),"Automatic stop explanation");
            var waking=new Frame{Utc=DateTime.UtcNow.ToString("o"),Connected=true,CanControl=false,Manual=false,Error="唤醒后温度暂不可用：Implausible CPU temperature; load prohibited.",Status="等待温度传感器恢复"};
            Require(Presentation.Describe(waking,null,false,0).Title=="等待温度传感器恢复","Wake-up sensor retry presentation");
            Require(Presentation.FanState(0,0,false)=="系统停转" && Presentation.FanState(0,30,true)=="等待起转" && Presentation.FanState(500,0,false)=="减速中","Stopped vs spinning up vs coast-down");
            live.Utc=DateTime.UtcNow.AddSeconds(-5).ToString("o");Require(!Presentation.IsLive(live),"Stale telemetry must not look live");
            Require(Presentation.Describe(live,null,false,0).Title.Contains("未连接"),"Stale telemetry explanation");
        }
        static void FrameLeaseTests(){
            double clock=0;var lease=new FrameLease(4,delegate{return clock;});
            var frame=new Frame{Session="test",Utc=DateTime.UtcNow.ToString("o"),Connected=true};
            Require(!lease.Observe(null,"test"),"Missing initial frame");Require(lease.Observe(frame,"test"),"Initial frame");
            clock=1;Require(lease.Observe(null,"test"),"Transient missing frame caused false trip");
            clock=3.9;Require(lease.Observe(frame,"test"),"Heartbeat expired too early");
            clock=4.1;Require(!lease.Observe(frame,"test"),"Replay renewed lease");
            frame.Utc=DateTime.UtcNow.AddMilliseconds(1).ToString("o");Require(lease.Observe(frame,"test"),"Advancing frame did not renew");
            clock=9;var foreign=new Frame{Session="foreign",Utc=DateTime.UtcNow.ToString("o"),Connected=true};Require(!lease.Observe(foreign,"test"),"Foreign frame renewed lease");
            frame.Utc=DateTime.UtcNow.AddMilliseconds(2).ToString("o");Require(lease.Observe(frame,"test"),"New frame rejected");
            frame.Ended=true;Require(!lease.Observe(frame,"test"),"Ended frame accepted");
        }
        static void Reject(Action action){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}Require(rejected,"Invalid policy accepted");}
        static void PolicyTests(){
            var p=new ControlPolicy(new Message{Command="SET",Percent=0},0);
            Require(p.Next(0,40,2600,2600)==0,"Stop request");
            Require(p.Next(1,50,0,0)==100 && p.SafetyStatus!=null,"Stop resumes with kickstart");
            Require(p.Next(4,49,2600,2600)==0,"Stop must release immediately below configured ceiling");
            Require(p.Next(5,45,2600,2600)==0,"Stop returns after cooling");
            p=new ControlPolicy(new Message{Command="SET",Percent=20},0);
            Require(p.Next(0,40,0,0)==100 && p.Next(2.9,40,500,400)==100 && p.Next(3,40,2500,2500)==20,"Spinup sequence");
            Require(p.Next(4,60,2500,2500)==30 && p.SafetyStatus!=null,"Low-output supplement instead of fault");
            Require(p.Next(5,59,2500,2500)==20 && p.Next(6,55,2500,2500)==20,"Low-output floor must release immediately below 60C");
            Require(p.Next(7,80,2500,2500)==100 && p.Next(8,79,2500,2500)==30,"High-temperature floor must release immediately below 80C");
            Require(p.Next(9,75,2500,2500)==30,"High-temperature supplement exit hysteresis");
            p=new ControlPolicy(new Message{Command="CURVE",Curve=new UserConfiguration().Curve},0);
            Require(p.Next(0,44,1500,1400)==0 && p.Next(120,44,0,0)==0,"Low curve interpolation must allow stop without timeout");
            p=new ControlPolicy(new Message{Command="SET",Percent=0,StopBelowC=60},0);
            Require(p.Next(0,55,0,0)==0 && p.Next(60,55,0,0)==0 && p.Next(61,60,0,0)==100,"Custom stop ceiling");
            p=new ControlPolicy(new Message{Command="SET",Percent=0,AllowFanStop=false},0);
            Require(p.Next(0,40,2000,2000)==20,"Stop disabled requests minimum running output");
            p=new ControlPolicy(new Message{Command="SET",Percent=20},0);
            p.Next(0,40,2000,2000);Require(p.Next(60,40,500,500)==20,"Sub-800 nonzero RPM must not fault");
            p=new ControlPolicy(new Message{Command="SET",Percent=20},0);p.Next(0,40,0,0);p.Next(3,40,0,0);
            Reject(delegate{p.Next(11,40,0,0);});
            p=new ControlPolicy(new Message{Command="SET",Percent=100},0);
            Require(p.Next(1,85,3000,3000)==100 && p.Next(2,94.99,3000,3000)==100,"Old 85C boundary incorrectly retained");
            Reject(delegate{p.Next(3,95,3000,3000);});Reject(delegate{p.Next(4,100,3000,3000);});Reject(delegate{p.Next(1,double.NaN,3000,3000);});
            var curve=MainForm.ParseCurve("35:0; 45:30; 55:50; 65:70; 75:100");ControlPolicy.ValidateCurve(curve);
            Require(ControlPolicy.Interpolate(curve,40)==15 && ControlPolicy.Interpolate(curve,10)==0 && ControlPolicy.Interpolate(curve,80)==100,"Curve interpolation/clamp");
            Reject(delegate{ControlPolicy.ValidateCurve(MainForm.ParseCurve("40:50; 35:100"));});
            Reject(delegate{ControlPolicy.ValidateCurve(MainForm.ParseCurve("40:60; 70:50"));});
            Reject(delegate{ControlPolicy.ValidateCurve(MainForm.ParseCurve("40:30; 81:100"));});
            Reject(delegate{ControlPolicy.ValidateCurve(MainForm.ParseCurve("40:30; 75:99"));});
            Reject(delegate{MainForm.ParseCurve("bad");});
            p=new ControlPolicy(new Message{Command="CURVE",Curve=curve},0);
            Require(p.Next(0,35,3000,3000)==0 && p.Next(1,75,3000,3000)==100,"Curve reacts to rising heat");
            p=new ControlPolicy(new Message{Command="RPM",Rpm=3000},0);
            Require(p.Next(0,40,3300,3000)==60 && p.Next(1,40,3300,3000)==60,"RPM deadband / slower fan");
            Require(p.Next(2,40,2000,2000)==62 && p.Next(3,40,4000,4000)==60,"RPM feedback direction");
            for(int i=4;i<90;i++)p.Next(i,40,1000,1000);
            Require(p.Next(90,40,1000,1000)==100,"RPM maximum clamp");
            Reject(delegate{new ControlPolicy(new Message{Command="RPM",Rpm=999},0);});
            Reject(delegate{new ControlPolicy(new Message{Command="RPM",Rpm=6001},0);});
            var guard=new LowSpeedGuard();ownGuard(guard);
        }
        static void ownGuard(LowSpeedGuard guard){
            var own=new Ownership{Active=true,OldRaw=-1,NewRaw=0};var f=new Frame{CpuC=49,Rpm1=0,Rpm2=0};
            Require(guard.Check(0,own,f)==null,"Zero allowed when cool");f.CpuC=50;
            Require(guard.Check(1,own,f)==null && guard.Check(3.9,own,f)==null && guard.Check(4,own,f)!=null,"Guardian failed-supplement grace / expiry");
            own.NewRaw=38;f.Rpm1=f.Rpm2=2500;Require(guard.Check(5,own,f)==null,"Guardian accepts supplemented output");
            own.NewRaw=1;f.CpuC=60;Require(guard.Check(6,own,f)==null && guard.Check(9,own,f)!=null,"Guardian low-output supplement failure");
            own.NewRaw=127;f.CpuC=90;Require(guard.Check(10,own,f)==null,"Guardian allows full airflow below 95C");
            own.NewRaw=100;f.CpuC=80;Require(guard.Check(11,own,f)==null && guard.Check(14,own,f)!=null,"Guardian checks high-temperature airflow");
            f.CpuC=40;f.Rpm1=f.Rpm2=0;Require(guard.Check(15,own,f)==null && guard.Check(27,own,f)!=null,"Guardian stall timeout");
            f.Rpm1=f.Rpm2=2000;Require(guard.Check(28,own,f)==null,"Guardian stall reset");
            f.Rpm1=f.Rpm2=500;Require(guard.Check(50,own,f)==null && guard.Check(100,own,f)==null,"Guardian accepts sub-800 nonzero RPM");
            own.NewRaw=0;own.StopBelowC=60;f.Rpm1=f.Rpm2=0;f.CpuC=55;Require(guard.Check(110,own,f)==null && guard.Check(300,own,f)==null,"Guardian intentional stop exemption and custom ceiling");
            own.Active=false;Require(guard.Check(99,own,f)==null,"Guardian must not claim automatic ownership");
        }
        static void StatelessRegressionTests(){
            var curve=new UserConfiguration().Curve;
            var p=new ControlPolicy(new Message{Command="CURVE",Curve=curve},0);
            p.Next(0,64.9,2000,2000);p.Next(1,50,2000,2000);
            Require(p.Next(2,47.9,2000,2000)==0 && p.RequestedOutputPercent==13 && p.RuleCode=="STOP" && p.SafetyStatus==null,"Reported stuck-airflow regression");
            Require(p.Next(3,50,2000,2000)==20 && p.Next(4,49.99,2000,2000)==0,"50C exact boundary / no extra 5C latch");
            Require(p.Next(5,80,2000,2000)==100 && p.Next(6,47.9,2000,2000)==0,"No hidden cooling accumulator after hot sample");
            p=new ControlPolicy(new Message{Command="SET",Percent=20},0);Require(p.Next(0,40,500,500)==20,"Already rotating low-speed fan must not get unnecessary startup boost");
            p=new ControlPolicy(new Message{Command="SET",Percent=0},0);Require(p.Next(0,50,0,0)==100 && p.Next(0.5,49.9,0,0)==0,"Return to stop cancels startup boost immediately");
            for(int ceiling=35;ceiling<=60;ceiling++)foreach(int minimum in new int[]{10,20,40})foreach(bool allow in new bool[]{false,true}){
                var m=new Message{Command="CURVE",Curve=curve,StopBelowC=ceiling,MinimumRunningPercent=minimum,AllowFanStop=allow};p=new ControlPolicy(m,0);double tick=0;
                for(int direction=0;direction<2;direction++)for(int index=0;index<=298;index++){
                    double t=direction==0?20+index*0.25:94.5-index*0.25;
                    int expected=ControlPolicy.Interpolate(curve,t);
                    if(expected<minimum)expected=allow && t<ceiling?0:minimum;
                    int floor=t>=80?100:t>=60?Math.Max(30,minimum):t>=ceiling?minimum:0;
                    expected=Math.Max(expected,floor);
                    Require(p.Next(tick++,t,2000,2000)==expected,"Temperature sweep mismatch "+ceiling+"/"+minimum+"/"+allow+" @ "+t);
                }
            }
            var own=new Ownership{Active=true,OldRaw=-1,NewRaw=38,StopBelowC=50,MinimumRunningPercent=40};
            var f=new Frame{CpuC=50,Rpm1=2000,Rpm2=2000};var guard=new LowSpeedGuard();
            Require(guard.Check(0,own,f)==null && guard.Check(3,own,f)!=null,"Guardian must enforce configured minimum, not fixed 30 percent");
            own.NewRaw=StrictSuperIoProbe.PercentToRaw(40);Require(guard.Check(4,own,f)==null,"Guardian accepts configured floor");
            f.CpuC=47.9;own.NewRaw=0;Require(guard.Check(5,own,f)==null,"Guardian agrees with worker's stop boundary");
            own.NewRaw=127;f.CpuC=94.99;Require(guard.Check(6,own,f)==null,"Guardian pre-emergency boundary");f.CpuC=95;Require(guard.Check(7,own,f)!=null,"Guardian 95C emergency");
        }
    }
}
