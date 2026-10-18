using System;
using System.IO;
namespace RedmiFanControl {
    public sealed class UserConfiguration {
        public int Version=1;public int Mode=2,Percent=60,Rpm=3000;
        public bool AllowFanStop=true;public int StopBelowC=50,MinimumRunningPercent=20;
        public CurvePoint[] Curve=MainForm.ParseCurve("35:0; 45:10; 55:20; 65:30; 75:40; 80:100");
        public Message Request(){return new Message{Command=Mode==0?"SET":Mode==1?"RPM":"CURVE",Percent=Percent,Rpm=Rpm,Curve=Curve,AllowFanStop=AllowFanStop,StopBelowC=StopBelowC,MinimumRunningPercent=MinimumRunningPercent};}
        public void Validate(){
            if(Version!=1 || Mode<0 || Mode>2 || Percent<0 || Percent>100 || (Rpm!=0 && (Rpm<1000 || Rpm>6000)))throw new ArgumentException("配置版本或数值无效。");
            ControlPolicy.ValidateCurve(Curve);new ControlPolicy(Request(),0);
        }
    }
    static class ConfigurationStore {
        public static string DefaultPath { get {return Path.Combine(Program.Root,"config","fan-control.json");} }
        public static UserConfiguration Load(string path,out string note){
            note="尚无已保存配置";if(!File.Exists(path))return new UserConfiguration();
            try{var value=Files.Read<UserConfiguration>(path);if(value==null)throw new ArgumentException("配置无法读取");value.Validate();note="已加载配置 · 启动仍为系统自动";return value;}
            catch(Exception ex){
                try{var backup=Files.Read<UserConfiguration>(path+".bak");if(backup!=null){backup.Validate();note="主配置无效，已加载上次备份";return backup;}}catch{}
                note="配置加载失败，使用默认值："+ex.Message;return new UserConfiguration();
            }
        }
        public static void Save(string path,UserConfiguration value){
            value.Validate();Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Never replace a good backup with corrupt input.
            if(File.Exists(path)){var previous=Files.Read<UserConfiguration>(path);if(previous!=null){try{previous.Validate();File.Copy(path,path+".bak",true);}catch(ArgumentException){}}}
            Files.Write(path,value);
        }
    }
}
