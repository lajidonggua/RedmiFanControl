using System;
using Microsoft.Win32;
namespace RedmiFanControl {
    interface IStartupStore { string Read();void Write(string command);void Remove(); }
    sealed class RegistryStartupStore : IStartupStore {
        const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run",Name="RedmiFanControl";
        public string Read(){using(var key=Registry.CurrentUser.OpenSubKey(KeyPath))return key==null?null:key.GetValue(Name) as string;}
        public void Write(string command){using(var key=Registry.CurrentUser.CreateSubKey(KeyPath))key.SetValue(Name,command,RegistryValueKind.String);}
        public void Remove(){using(var key=Registry.CurrentUser.OpenSubKey(KeyPath,true))if(key!=null)key.DeleteValue(Name,false);}
    }
    sealed class MemoryStartupStore : IStartupStore {
        public string Value;public string Read(){return Value;}public void Write(string command){Value=command;}public void Remove(){Value=null;}
    }
    static class Autostart {
        public static string Command(string executable){
            if(String.IsNullOrWhiteSpace(executable) || executable.IndexOfAny(new char[]{'"','\r','\n'})>=0 || !System.IO.Path.IsPathRooted(executable))throw new ArgumentException("自启程序路径无效。");
            string value="\""+executable+"\" --startup";
            if(value.Length>260)throw new ArgumentException("程序路径太长，请先移动到较短的固定目录再开启自启。");return value;
        }
        public static void Set(IStartupStore store,string executable,bool enabled){
            if(enabled)store.Write(Command(executable));else store.Remove();
            if(enabled && store.Read()!=Command(executable) || !enabled && store.Read()!=null)throw new InvalidOperationException("开机自启设置未确认。");
        }
    }
}
