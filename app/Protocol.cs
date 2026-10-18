using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Web.Script.Serialization;
namespace RedmiFanControl {
    public sealed class Message {
        public string Session; public int Sequence; public string Command; public int Percent;
        public int Rpm; public CurvePoint[] Curve;
        public bool AllowFanStop=true; public int StopBelowC=50; public int MinimumRunningPercent=20;
        public long Pid; public long StartTicks;
    }
    public sealed class Ownership {
        public string Session; public bool Active; public int OldRaw; public int NewRaw;
        public int StopBelowC=50;
        public int MinimumRunningPercent=20;
        public int[] Targets(){return OldRaw>=0 && OldRaw!=NewRaw ? new int[]{OldRaw,NewRaw} : new int[]{NewRaw};}
    }
    public sealed class Frame {
        public string Session; public string Utc; public bool Connected; public bool CanControl;
        public bool Manual; public bool Owned; public int SetPercent; public int Ack;
        public int Rpm1; public int Rpm2; public double Output1; public double Output2;
        public int Target1; public int Target2;
        public string ControlMode; public int RequestedRpm;
        public string ActiveSettingsKey;
        public string SafetyStatus;
        public int RequestedOutputPercent;public string DecisionReason;public string RuleCode;
        public double? CpuC; public string Status; public string Error;
        public bool Ended; public bool AutoConfirmed;
    }
    public static class Files {
        public static void Write(string path,object value){
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(value),new UTF8Encoding(false));
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        public static T Read<T>(string path) where T:class {
            try{if(!File.Exists(path) || new FileInfo(path).Length>32768)return null;return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path));}
            catch(IOException){return null;}catch(ArgumentException){return null;}
        }
        public static void Event(string path,string kind,object value){
            File.AppendAllText(path,new JavaScriptSerializer().Serialize(new {utc=DateTime.UtcNow.ToString("o"),kind=kind,value=value})+Environment.NewLine,new UTF8Encoding(false));
        }
        // Recovery must never depend on writable log/session files.
        public static bool TryWrite(string path,object value){try{Write(path,value);return true;}catch{return false;}}
        public static void TryEvent(string path,string kind,object value){try{Event(path,kind,value);}catch{}}
        public static bool Alive(long pid,long ticks){try{using(var p=Process.GetProcessById((int)pid)){return !p.HasExited && p.StartTime.ToUniversalTime().Ticks==ticks;}}catch{return false;}}
        public static bool Fresh(string utc,int seconds){DateTime value;return DateTime.TryParse(utc,out value) && Math.Abs((DateTime.UtcNow-value.ToUniversalTime()).TotalSeconds)<seconds;}
    }
    public sealed class Lease {
        int last=-1; Stopwatch since=Stopwatch.StartNew(); bool seen;
        public bool Observe(Message message,string session){
            if(message!=null && message.Session==session && message.Sequence>last){last=message.Sequence;seen=true;since.Restart();}
            return seen && since.Elapsed.TotalSeconds<4;
        }
    }
    // A transient missing file must not immediately mean a dead process. Only
    // advancing, fresh, same-session telemetry renews this monotonic deadline.
    public sealed class FrameLease {
        readonly double timeout; readonly Func<double> now;
        double received; DateTime lastUtc=DateTime.MinValue; bool seen;
        public FrameLease(double seconds):this(seconds,MonotonicClock()){}
        internal FrameLease(double seconds,Func<double> clock){timeout=seconds;now=clock;}
        static Func<double> MonotonicClock(){var watch=Stopwatch.StartNew();return delegate{return watch.Elapsed.TotalSeconds;};}
        public bool Observe(Frame frame,string session){
            if(frame!=null && frame.Session==session && frame.Ended){seen=false;return false;}
            DateTime stamp;
            if(frame!=null && frame.Session==session && frame.Connected && !frame.Ended &&
                DateTime.TryParse(frame.Utc,out stamp) && Files.Fresh(frame.Utc,(int)Math.Ceiling(timeout)) && stamp.ToUniversalTime()>lastUtc){
                lastUtc=stamp.ToUniversalTime();received=now();seen=true;
            }
            return seen && now()-received<timeout;
        }
    }
}
