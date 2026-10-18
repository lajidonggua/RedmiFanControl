using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Security.Cryptography;
namespace RedmiFanControl {
    static class Program {
        public static string Root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..",".."));
        [STAThread] public static int Main(string[] args){
            try {
                if(args.Length>0 && args[0]=="--worker")return Backend.Run(args[1],args[2],long.Parse(args[3]),long.Parse(args[4]),false);
                if(args.Length>0 && args[0]=="--guardian")return Backend.Run(args[1],args[2],long.Parse(args[3]),long.Parse(args[4]),true);
                if(args.Length>0 && args[0]=="--selftest")return AppTests.Run();
                if(args.Length>0 && args[0]=="--smoke")return SmokeTest.Run(args[1]);
                if(args.Length>0 && args[0]=="--recovery-test")return SmokeTest.RecoveryTest(args[1]);
                if(args.Length>0 && args[0]=="--stop-test")return SmokeTest.StopTest(args[1]);
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                if(args.Length==2 && args[0]=="--preview-curve"){using(var editor=new CurveEditor(new UserConfiguration().Curve))editor.SavePreview(args[1]);return 0;}
                bool preview=args.Length>0 && args[0]=="--preview";
                bool startup=args.Length==1 && args[0]=="--startup";
                using(var singleton=new Mutex(false,"Local\\RedmiFanControl.TM2113.UI")){
                    if(!preview && !singleton.WaitOne(0)){if(startup)return 0;MessageBox.Show("Redmi Fan Control 已在运行。请查看任务栏或托盘。","Redmi Fan Control");return 1;}
                    using(var form=new MainForm(preview)){
                        if(preview){form.SavePreview(args[1]);return 0;}
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
