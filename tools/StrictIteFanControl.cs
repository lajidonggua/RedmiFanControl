using System;
using System.Threading;
namespace EcResearch {
    public sealed partial class StrictSuperIoProbe {
        public sealed class ControlState {
            public int Flags,Target1,Target2,Pwm1,Pwm2,Rpm1,Rpm2;
            public bool Manual;
        }
        private void FixedControlWrite(ushort address,byte value) {
            // Exact vendor control bytes only; no arbitrary EC/PWM writer.
            if(address!=0x0100 && address!=0x0105 && address!=0x0106)
                throw new InvalidOperationException("Fan-control address allowlist violation.");
            AddressLatch(0x11,(byte)(address>>8));AddressLatch(0x10,(byte)(address&255));
            if(DepthRead(0x11)!=(byte)(address>>8) || DepthRead(0x10)!=(byte)(address&255))
                throw new InvalidOperationException("Control address latch did not echo.");
            DepthSelect(0x12);
            Call("ioctl_superio_outb",new ulong[]{0x2F,value},0);
            if(FanRamByte(address)!=value)throw new InvalidOperationException("Control byte did not echo.");
        }
        private T ControlSession<T>(Func<T> action) {
            bool locked=false,entered=false,saved=false;
            byte selector=0,high=0,low=0;
            try {
                try{locked=mutex.WaitOne(1000);}catch(AbandonedMutexException){locked=true;}
                if(!locked)throw new TimeoutException("ISA bus mutex busy.");
                indexPort=0x4E;Call("ioctl_select_slot",new ulong[]{1},0);
                Key(0x87);Key(0x01);Key(0x55);Key(0xAA);entered=true;
                if(((Read(0x20)<<8)|Read(0x21))!=0x5571)throw new InvalidOperationException("IT5571 required.");
                selector=Read(0x2E);high=DepthRead(0x11);low=DepthRead(0x10);saved=true;
                if(FanRamByte(0x0400)!=0x50 || FanRamByte(0x0401)!=1 || FanRamByte(0x0402)!=0x11)
                    throw new InvalidOperationException("P.11 EC version required.");
                if(FanRamByte(0x2000)!=0x55 || FanRamByte(0x2001)!=0x71 || (FanRamByte(0x200D)&3)!=3)
                    throw new InvalidOperationException("Existing I2EC write access and matching EC ID required; permissions will not be changed.");
                return action();
            }finally {
                try{if(saved){AddressLatch(0x11,high);AddressLatch(0x10,low);Call("ioctl_superio_outb",new ulong[]{0x2E,selector},0);}}
                finally {
                    try{if(entered){Key(2);Call("ioctl_pio_outb",new ulong[]{0x4F,2},0);}}
                    finally{if(locked)mutex.ReleaseMutex();}
                }
            }
        }
        private ControlState CurrentControlState() {
            return CurrentControlState(true);
        }
        private ControlState CurrentControlState(bool validatePwm) {
            if(validatePwm && (FanRamByte(0x1841)!=127 || FanRamByte(0x185B)!=0 || FanRamByte(0x185C)!=0 || FanRamByte(0x185D)!=0 || FanRamByte(0x180A)!=0 || FanRamByte(0x180F)!=0))
                throw new InvalidOperationException("PWM configuration differs from the validated device.");
            int flags=FanRamByte(0x0100);
            return new ControlState{Flags=flags,Manual=(flags&8)!=0,Target1=FanRamByte(0x0105),Target2=FanRamByte(0x0106),
                Pwm1=FanRamByte(0x1804),Pwm2=FanRamByte(0x1805),Rpm1=StableFanWord(0x0298),Rpm2=StableFanWord(0x02B8)};
        }
        public ControlState ReadControlState() {return ControlSession<ControlState>(CurrentControlState);}
        public static int PercentToRaw(int percent) {
            if(percent<0 || percent>100)throw new ArgumentOutOfRangeException("percent","Manual range is 0 to 100 percent.");
            return percent==100 ? 127 : Math.Min(126,(int)Math.Round(percent*128.0/100.0,MidpointRounding.AwayFromZero));
        }
        public static double RawToPercent(int raw) {if(raw<0 || raw>127)throw new ArgumentOutOfRangeException("raw");return raw==127 ? 100.0 : Math.Round(raw*100.0/128.0,1);}
        public ControlState SetLinkedPercent(int percent,int previousRaw) {
            int target=PercentToRaw(percent);
            return ControlSession<ControlState>(delegate(){
                var before=CurrentControlState();
                if(before.Manual && (previousRaw<0 || before.Target1!=previousRaw || before.Target2!=previousRaw))
                    throw new InvalidOperationException("Manual control belongs to another controller.");
                if(FanRamByte(0x1841)!=127 || FanRamByte(0x185B)!=0 || FanRamByte(0x185C)!=0 || FanRamByte(0x185D)!=0 || FanRamByte(0x180A)!=0 || FanRamByte(0x180F)!=0)
                    throw new InvalidOperationException("PWM configuration differs from the validated device.");
                FixedControlWrite(0x0105,(byte)target);FixedControlWrite(0x0106,(byte)target);
                byte flags=FanRamByte(0x0100);
                if(flags!=FanRamByte(0x0100))throw new InvalidOperationException("Control flags changed while staging.");
                FixedControlWrite(0x0100,(byte)(flags|8));
                return CurrentControlState();
            });
        }
        public ControlState RestoreAutomaticForOwnedTargets(int[] allowedTargets) {
            if(allowedTargets==null || allowedTargets.Length==0 || allowedTargets.Length>3)
                throw new ArgumentException("Owned targets required.");
            foreach(int value in allowedTargets)if(value<0 || value>127)throw new ArgumentOutOfRangeException("allowedTargets");
            return ControlSession<ControlState>(delegate(){
                var state=CurrentControlState(false);
                if(state.Manual && (Array.IndexOf(allowedTargets,state.Target1)<0 || Array.IndexOf(allowedTargets,state.Target2)<0))
                    throw new InvalidOperationException("External controller detected; refusing to overwrite its settings.");
                if(!state.Manual && state.Target1==0 && state.Target2==0)return state;
                byte flags=FanRamByte(0x0100);
                FixedControlWrite(0x0100,(byte)(flags&0xF7));FixedControlWrite(0x0105,0);FixedControlWrite(0x0106,0);
                var restored=CurrentControlState(false);
                if(restored.Manual || restored.Target1!=0 || restored.Target2!=0)throw new InvalidOperationException("Automatic recovery not confirmed.");
                return restored;
            });
        }
        public ControlState EnableFullSpeedForTest() {
            return ControlSession<ControlState>(delegate(){
                var before=CurrentControlState();
                if(before.Manual)throw new InvalidOperationException("Already in manual mode; refusing to take over another controller.");
                if(FanRamByte(0x1841)!=127 || FanRamByte(0x185B)!=0 || FanRamByte(0x185C)!=0 || FanRamByte(0x185D)!=0 || FanRamByte(0x180A)!=0 || FanRamByte(0x180F)!=0)
                    throw new InvalidOperationException("PWM cycle/polarity/gating differs from validated configuration.");
                // Stage both full-speed targets before enabling the common manual flag.
                FixedControlWrite(0x0105,127);FixedControlWrite(0x0106,127);
                byte flags=FanRamByte(0x0100);
                if(flags!=FanRamByte(0x0100) || (flags&8)!=0)throw new InvalidOperationException("Control flags changed while staging.");
                // Preserve all unrelated status bits; never restore an old entire flags byte.
                FixedControlWrite(0x0100,(byte)(flags|8));
                return CurrentControlState();
            });
        }
        public ControlState RestoreAutomaticAfterFullSpeedTest() {
            return ControlSession<ControlState>(delegate(){
                var state=CurrentControlState(false);
                if(state.Manual && (state.Target1!=127 || state.Target2!=127))
                    throw new InvalidOperationException("Manual targets no longer belong to this full-speed test; external controller may be active.");
                byte flags=FanRamByte(0x0100);
                FixedControlWrite(0x0100,(byte)(flags&0xF7));
                FixedControlWrite(0x0105,0);FixedControlWrite(0x0106,0);
                var restored=CurrentControlState(false);
                if(restored.Manual || restored.Target1!=0 || restored.Target2!=0)throw new InvalidOperationException("Automatic mode restoration not confirmed.");
                return restored;
            });
        }
    }
}
