using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Security.Cryptography;
namespace RedmiFanControl {
    static class Program {
        public static string Root=AppDomain.CurrentDomain.BaseDirectory;
        [STAThread] public static int Main(string[] args){
            try {
                if(args.Length>0 && args[0]=="--worker")return Backend.Run(args[1],args[2],long.Parse(args[3]),long.Parse(args[4]),false);
                if(args.Length>0 && args[0]=="--guardian")return Backend.Run(args[1],args[2],long.Parse(args[3]),long.Parse(args[4]),true);
                if(args.Length>0 && args[0]=="--selftest")return AppTests.Run(args.Length>1?args[1]:Path.Combine(Path.GetTempPath(),"RedmiFanControl-test-"+Guid.NewGuid().ToString("N")));
                if(args.Length==2 && args[0]=="--export-runtime"){RuntimeModules.Prepare(Path.GetFullPath(args[1]));return 0;}
                if(args.Length>0 && args[0]=="--smoke")return SmokeTest.Run(args[1]);
                if(args.Length>0 && args[0]=="--recovery-test")return SmokeTest.RecoveryTest(args[1]);
                if(args.Length>0 && args[0]=="--stop-test")return SmokeTest.StopTest(args[1]);
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                if(args.Length==2 && args[0]=="--preview-curve"){using(var editor=new CurveEditor(new UserConfiguration().Curve))editor.SavePreview(args[1]);return 0;}
                bool preview=args.Length==2 && (args[0]=="--preview" || args[0]=="--preview-stopped" || args[0]=="--preview-percent" || args[0]=="--preview-offline" || args[0]=="--preview-maximized");
                bool startup=args.Length==1 && args[0]=="--startup";
                using(var singleton=new Mutex(false,"Local\\RedmiFanControl.TM2113.UI")){
                    bool ownsMutex=false;
                    if(!preview){try{ownsMutex=singleton.WaitOne(0);}catch(AbandonedMutexException){ownsMutex=true;}}
                    if(!preview && !ownsMutex){
                        if(startup)return 0;
                        try{using(var signal=EventWaitHandle.OpenExisting("Local\\RedmiFanControl.TM2113.Show"))signal.Set();}
                        catch(WaitHandleCannotBeOpenedException){MessageBox.Show("旧版程序已在运行，请从任务栏或托盘打开。更新前请正常退出旧版。","Redmi Fan Control");}
                        return 0;
                    }
                    using(var signal=preview?null:new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\RedmiFanControl.TM2113.Show"))
                    using(var form=new MainForm(preview)){
                        if(preview){form.SavePreview(args[1],args[0]);return 0;}
                        form.WakeSignal=signal;
                        form.StartInTray=startup;
                        // Explicit one-shot user-authorized launch; ordinary launches stay automatic.
                        form.ApplyDefaultCurveOnConnect=args.Length==1 && args[0]=="--apply-default-curve";
                        Application.Run(form);
                    }
                    if(!preview)singleton.ReleaseMutex();
                }
                return 0;
            }catch(Exception ex){
                if(args.Length>0 && (args[0]=="--worker" || args[0]=="--guardian")){
                    try{Files.Event(Path.Combine(args[1],"fatal.jsonl"),args[0],ex.ToString());}catch{}
                }else MessageBox.Show(ex.Message,"Redmi Fan Control",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
        public static string Token(){byte[] bytes=new byte[24];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return BitConverter.ToString(bytes).Replace("-","");}
        public static string Quote(string text){if(text.IndexOf('"')>=0)throw new ArgumentException("Invalid path");return "\""+text+"\"";}
        public static Process Spawn(string role,string run,string session,long pid,long ticks,bool elevate){
            var info=new ProcessStartInfo(Application.ExecutablePath,role+" "+Quote(run)+" "+session+" "+pid+" "+ticks);
            info.WorkingDirectory=Root;info.UseShellExecute=true;info.WindowStyle=ProcessWindowStyle.Hidden;
            if(elevate)info.Verb="runas";
            return Process.Start(info);
        }
    }
}
