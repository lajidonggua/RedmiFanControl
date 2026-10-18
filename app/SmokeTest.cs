using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
namespace RedmiFanControl {
    static class SmokeTest {
        static string run,session;static int sequence;static volatile bool pulse;
        static void Event(string name,object value){Files.Event(Path.Combine(run,"smoke.jsonl"),name,value);}
        static Frame Frame(){return Files.Read<Frame>(Path.Combine(run,"state.json"));}
        static Frame Wait(Func<Frame,bool> condition,int seconds){
            var watch=Stopwatch.StartNew();
            while(watch.Elapsed.TotalSeconds<seconds){var frame=Frame();if(frame!=null && frame.Session==session && condition(frame))return frame;Thread.Sleep(150);}
            throw new TimeoutException("Integration check timed out.");
        }
        static void Command(string name,int percent){Files.Write(Path.Combine(run,"command.json"),new Message{Session=session,Sequence=Interlocked.Increment(ref sequence),Command=name,Percent=percent});}
        public static int StopTest(string directory){
            run=Path.GetFullPath(directory);Directory.CreateDirectory(run);session=Program.Token();Process worker=null;Thread beat=null;
            try{
                pulse=true;beat=new Thread(delegate(){while(pulse){try{Files.Write(Path.Combine(run,"heartbeat.json"),new Message{Session=session,Sequence=Interlocked.Increment(ref sequence)});}catch{pulse=false;}Thread.Sleep(200);}});beat.IsBackground=true;beat.Start();
                using(var parent=Process.GetCurrentProcess())worker=Program.Spawn("--worker",run,session,parent.Id,parent.StartTime.ToUniversalTime().Ticks,false);
                var before=Wait(delegate(Frame f){return f.Connected && f.CanControl && !f.Manual;},15);Event("baseline",before);
                if(!before.CpuC.HasValue || before.CpuC>=45){Event("SKIP_warm",new {reason="CPU must be below 45C for this bounded stop test",temperature=before.CpuC});return 2;}
                Command("SET",0);var watch=Stopwatch.StartNew();bool stopped=false;
                while(watch.Elapsed.TotalSeconds<12){
                    var f=Frame();if(f!=null && f.Session==session){
                        Event("stop_sample",f);
                        if(f.Error!=null || f.Ended || !f.CpuC.HasValue || f.CpuC>=48)throw new InvalidOperationException("Bounded stop test aborted: thermal/sensor/backend condition changed.");
                        if(f.Manual && f.Owned && f.SetPercent==0 && f.Target1==0 && f.Target2==0 && f.Rpm1==0 && f.Rpm2==0)stopped=true;
                    }Thread.Sleep(500);
                }
                Command("AUTO",0);Event("automatic_restored",Wait(delegate(Frame f){return f.Connected && !f.Manual && !f.Owned && f.AutoConfirmed;},10));
                if(!stopped)throw new InvalidOperationException("Zero target written but complete physical coast-down was not observed within 12 seconds.");
                Event("PASS_stop_and_auto",null);return 0;
            }catch(Exception ex){Event("FAIL_or_aborted",ex.ToString());return 1;}
            finally{
                try{Command("AUTO",0);Thread.Sleep(700);Command("QUIT",0);if(worker!=null)worker.WaitForExit(12000);}catch{}
                var own=Files.Read<Ownership>(Path.Combine(run,"ownership.json"));
                if(own!=null && own.Session==session && own.Active){try{using(var hw=new Hardware()){Event("parent_backup_restore",hw.Auto(own));own.Active=false;Files.Write(Path.Combine(run,"ownership.json"),own);}}catch(Exception ex){Event("backup_restore_error",ex.ToString());}}
                pulse=false;if(beat!=null)beat.Join(2000);if(worker!=null)worker.Dispose();
            }
        }
        public static int Run(string directory){
            run=Path.GetFullPath(directory);Directory.CreateDirectory(run);session=Program.Token();Process worker=null;Thread heartbeat=null;
            try{
                pulse=true;heartbeat=new Thread(delegate(){while(pulse){Files.Write(Path.Combine(run,"heartbeat.json"),new Message{Session=session,Sequence=Interlocked.Increment(ref sequence)});Thread.Sleep(200);}});
                heartbeat.IsBackground=true;heartbeat.Start();
                using(var parent=Process.GetCurrentProcess())worker=Program.Spawn("--worker",run,session,parent.Id,parent.StartTime.ToUniversalTime().Ticks,false);
                var before=Wait(delegate(Frame f){return f.Connected && f.CanControl && !f.Manual;},15);Event("baseline",before);
                Command("SET",60);
                var applied=Wait(delegate(Frame f){return f.Owned && f.Manual && f.SetPercent==60 && f.Output1>59 && f.Output1<62 && f.Output2>59 && f.Output2<62;},8);
                Event("manual_60",applied);Thread.Sleep(3500);Event("manual_60_settled",Frame());
                Command("SET",100);
                applied=Wait(delegate(Frame f){return f.Owned && f.Manual && f.SetPercent==100 && f.Output1==100 && f.Output2==100;},8);
                Event("manual_100",applied);Thread.Sleep(2500);Event("manual_100_settled",Frame());
                Command("AUTO",0);var auto=Wait(delegate(Frame f){return f.Connected && !f.Manual && !f.Owned && f.AutoConfirmed;},8);Event("automatic_restored",auto);
                // Verify lost-window recovery with a fresh manual lease. No process is killed.
                Command("SET",100);Wait(delegate(Frame f){return f.Manual && f.Owned && f.Output1==100;},8);
                pulse=false;heartbeat.Join(1500);Event("heartbeat_stopped",null);
                var ended=Wait(delegate(Frame f){return f.Ended && !f.Owned && f.AutoConfirmed && !f.Manual;},12);Event("lost_window_auto",ended);
                Event("PASS",new {temperature=ended.CpuC,worker_pid=worker.Id});return 0;
            }catch(Exception ex){
                Event("FAIL",ex.ToString());
                try{pulse=true;if(heartbeat==null || !heartbeat.IsAlive){heartbeat=new Thread(delegate(){for(int n=0;n<50 && pulse;n++){Files.Write(Path.Combine(run,"heartbeat.json"),new Message{Session=session,Sequence=Interlocked.Increment(ref sequence)});Thread.Sleep(150);}});heartbeat.IsBackground=true;heartbeat.Start();}Command("AUTO",0);Thread.Sleep(3000);Command("QUIT",0);}catch{}
                return 1;
            }finally{pulse=false;if(heartbeat!=null)heartbeat.Join(2000);if(worker!=null)worker.Dispose();}
        }
        public static int RecoveryTest(string directory){
            run=Path.GetFullPath(directory);Directory.CreateDirectory(run);session=Program.Token();Process worker=null;Thread beat=null;
            try{
                pulse=true;beat=new Thread(delegate(){while(pulse){Files.Write(Path.Combine(run,"heartbeat.json"),new Message{Session=session,Sequence=Interlocked.Increment(ref sequence)});Thread.Sleep(200);}});beat.IsBackground=true;beat.Start();
                using(var parent=Process.GetCurrentProcess())worker=Program.Spawn("--worker",run,session,parent.Id,parent.StartTime.ToUniversalTime().Ticks,false);
                Event("baseline",Wait(delegate(Frame f){return f.Connected && f.CanControl && !f.Manual;},15));
                Command("SET",100);Event("manual_100",Wait(delegate(Frame f){return f.Manual && f.Owned && f.Output1==100 && f.Output2==100;},8));
                Thread.Sleep(750);
                // Terminate only this newly spawned test worker. Guardian remains independent.
                worker.Kill();worker.WaitForExit(3000);Event("owned_worker_terminated",new {pid=worker.Id});
                var wait=Stopwatch.StartNew();Frame recovery=null;
                while(wait.Elapsed.TotalSeconds<8){recovery=Files.Read<Frame>(Path.Combine(run,"guardian.json"));if(recovery!=null && recovery.Session==session && recovery.Ended && recovery.AutoConfirmed && !recovery.Manual)break;Thread.Sleep(150);}
                if(recovery==null || !recovery.Ended || !recovery.AutoConfirmed || recovery.Manual)throw new InvalidOperationException("Independent guardian recovery not confirmed.");
                Event("PASS_independent_recovery",recovery);return 0;
            }catch(Exception ex){Event("FAIL",ex.ToString());return 1;}
            finally{
                // An extra, independent parent-side recovery attempt if the guardian test failed.
                var own=Files.Read<Ownership>(Path.Combine(run,"ownership.json"));
                if(own!=null && own.Session==session && own.Active){try{using(var hw=new Hardware()){Event("parent_backup_restore",hw.Auto(own));own.Active=false;Files.Write(Path.Combine(run,"ownership.json"),own);}}catch(Exception ex){Event("backup_restore_error",ex.ToString());}}
                pulse=false;if(beat!=null)beat.Join(2000);if(worker!=null)worker.Dispose();
            }
        }
    }
}
