using System;
using System.IO;
namespace RedmiFanControl {
    public enum CloseBehavior {
        Ask=0,
        HideToTray=1,
        Exit=2
    }
    public sealed class UiPreferences {
        public int Version=1;
        public CloseBehavior CloseBehavior=CloseBehavior.Ask;
        public bool LastInTray;
        public void Validate(){
            if(Version!=1 || CloseBehavior<CloseBehavior.Ask || CloseBehavior>CloseBehavior.Exit)throw new ArgumentException("界面偏好版本或关闭行为无效。");
        }
    }
    static class UiPreferencesStore {
        public static string DefaultPath { get {return AppPaths.UiPreferences;} }
        public static UiPreferences LoadDefault(out string note){return Load(DefaultPath,out note);}
        public static UiPreferences Load(string path,out string note){
            note="尚无界面偏好，点击 × 时会询问";
            if(!File.Exists(path))return new UiPreferences();
            try{
                var value=Files.Read<UiPreferences>(path);if(value==null)throw new ArgumentException("界面偏好无法读取");value.Validate();
                note="已加载界面偏好";return value;
            }catch(Exception ex){
                try{var backup=Files.Read<UiPreferences>(path+".bak");if(backup!=null){backup.Validate();note="主界面偏好无效，已加载上次备份";return backup;}}catch{}
                note="界面偏好加载失败，使用默认值："+ex.Message;return new UiPreferences();
            }
        }
        public static void Save(string path,UiPreferences value){
            value.Validate();Directory.CreateDirectory(Path.GetDirectoryName(path));
            if(File.Exists(path)){
                var previous=Files.Read<UiPreferences>(path);
                if(previous!=null){try{previous.Validate();File.Copy(path,path+".bak",true);}catch(ArgumentException){}}
            }
            Files.Write(path,value);
        }
    }
}
