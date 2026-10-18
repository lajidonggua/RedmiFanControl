$ErrorActionPreference='Stop'
$source=(Get-Content (Join-Path $PSScriptRoot '..\tools\StrictSuperIoProbe.cs') -Raw)+[Environment]::NewLine+(Get-Content (Join-Path $PSScriptRoot '..\tools\StrictIteFanControl.cs') -Raw)
# Concatenate source files without their second top-level using directives.
$source=$source.Replace([Environment]::NewLine+'using System;'+[Environment]::NewLine+'using System.Threading;','')
$test=@'
namespace EcResearch {
    public static class IteControlTests {
        sealed class Fake {
            public byte selector=0x44,hi=3,lo=0x45;
            public bool exited;
            public Dictionary<int,int> ram=new Dictionary<int,int>{{0x0100,0x51},{0x0105,0},{0x0106,0},{0x0400,0x50},{0x0401,1},{0x0402,0x11},
                {0x2000,0x55},{0x2001,0x71},{0x200D,0xC3},{0x1804,39},{0x1805,44},{0x1841,127},{0x185B,0},{0x185C,0},{0x185D,0},{0x180A,0},{0x180F,0},
                {0x0298,0x0A},{0x0299,0x50},{0x02B8,0x0A},{0x02B9,0x60}};
            public List<int> written=new List<int>();
            public ulong[] Invoke(string name,ulong[] args,int count) {
                if(name=="ioctl_select_slot")return new ulong[0];
                if(name=="ioctl_pio_outb") {if(args[0]==0x4F && args[1]==2)exited=true;return new ulong[0];}
                if(name=="ioctl_superio_outb") {
                    if(args[0]==0x2E)selector=(byte)args[1];
                    else if(args[0]==0x2F) {
                        if(selector==0x10)lo=(byte)args[1];else if(selector==0x11)hi=(byte)args[1];
                        else if(selector==0x12) {
                            int address=(hi<<8)|lo;
                            if(address!=0x0100 && address!=0x0105 && address!=0x0106)throw new Exception("Forbidden EC write");
                            written.Add(address);ram[address]=(int)args[1];
                            if(address==0x0100){ram[0x1804]=(ram[address]&8)!=0?ram[0x0105]:39;ram[0x1805]=(ram[address]&8)!=0?ram[0x0106]:44;}
                        }else throw new Exception("Bad depth selector");
                    }else throw new Exception("Forbidden configuration write");
                    return new ulong[0];
                }
                if(name!="ioctl_superio_inb")throw new Exception("Bad operation");
                int value;
                switch(args[0]) {
                    case 0x20:value=0x55;break;case 0x21:value=0x71;break;case 0x2E:value=selector;break;
                    case 0x2F:
                        if(selector==0x10)value=lo;else if(selector==0x11)value=hi;
                        else if(selector==0x12)value=ram[(hi<<8)|lo];else throw new Exception("Bad read selector");break;
                    default:throw new Exception("Unexpected read");
                }
                return new ulong[]{(ulong)value};
            }
        }
        public static void Run() {
            var f=new Fake();
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                var state=p.EnableFullSpeedForTest();
                if(!state.Manual || state.Target1!=127 || state.Target2!=127 || state.Pwm1!=127 || state.Flags!=0x59)throw new Exception("Full-speed transition failed");
                if(f.written[0]!=0x0105 || f.written[1]!=0x0106 || f.written[2]!=0x0100)throw new Exception("Unsafe write order");
                f.ram[0x0100]|=0x80; // Firmware changed an unrelated flag during the test.
                state=p.RestoreAutomaticAfterFullSpeedTest();
                if(state.Manual || state.Target1!=0 || state.Target2!=0 || state.Flags!=0xD1 || state.Pwm1!=39)throw new Exception("Automatic restoration clobbered unrelated flags");
            }
            if(!f.exited || f.selector!=0x44 || f.hi!=3 || f.lo!=0x45)throw new Exception("Session cleanup failed");
            foreach(int scenario in new int[]{0,1,2,3}) {
                f=new Fake();
                if(scenario==0)f.ram[0x0100]|=8;
                if(scenario==1)f.ram[0x0402]=0;
                if(scenario==2)f.ram[0x200D]=2;
                if(scenario==3)f.ram[0x1841]=255;
                using(var p=new StrictSuperIoProbe(f.Invoke)) {
                    try{p.EnableFullSpeedForTest();throw new Exception("Guard bypassed");}catch(InvalidOperationException){}
                }
                if(f.written.Count!=0 || !f.exited)throw new Exception("Guard performed a write");
            }
            f=new Fake();f.ram[0x0100]|=8;f.ram[0x0105]=80;
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                try{p.RestoreAutomaticAfterFullSpeedTest();throw new Exception("External control overwritten");}catch(InvalidOperationException){}
            }
            if(f.written.Count!=0)throw new Exception("External controller guard failed");
            f=new Fake();
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                var state=p.SetLinkedPercent(60,-1);
                if(state.Target1!=77 || state.Target2!=77 || !state.Manual)throw new Exception("60 percent transition");
                state=p.SetLinkedPercent(80,77);
                if(state.Target1!=102 || state.Target2!=102)throw new Exception("Owned percent transition");
                state=p.RestoreAutomaticForOwnedTargets(new int[]{77,102});
                if(state.Manual || state.Target1!=0 || state.Target2!=0)throw new Exception("Percent automatic recovery");
            }
            f=new Fake();
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                foreach(int bad in new int[]{-1,101,255}){
                    try{p.SetLinkedPercent(bad,-1);throw new Exception("Invalid range accepted");}catch(ArgumentOutOfRangeException){}
                }
            }
            if(f.written.Count!=0)throw new Exception("Range check performed writes");
            f=new Fake();
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                int previous=-1;
                for(int percent=0;percent<=100;percent++){
                    var state=p.SetLinkedPercent(percent,previous);previous=StrictSuperIoProbe.PercentToRaw(percent);
                    if(!state.Manual || state.Target1!=previous || state.Target2!=previous)throw new Exception("Full range write mismatch");
                }
                p.SetLinkedPercent(0,previous);
                var restored=p.RestoreAutomaticForOwnedTargets(new int[]{0});
                if(restored.Manual || restored.Target1!=0 || restored.Target2!=0)throw new Exception("Manual-zero recovery failed");
                p.SetLinkedPercent(0,-1);p.SetLinkedPercent(1,0);
                restored=p.RestoreAutomaticForOwnedTargets(new int[]{0,1});
                if(restored.Manual)throw new Exception("Zero-to-low transition recovery failed");
            }
            f=new Fake();
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                p.SetLinkedPercent(60,-1);f.ram[0x1841]=255;
                var state=p.RestoreAutomaticForOwnedTargets(new int[]{77});
                if(state.Manual || state.Target1!=0 || state.Target2!=0)throw new Exception("Changed PWM cycle prevented safe auto recovery");
            }
        }
    }
}
'@
# Using directives from the partial file must precede all namespace bodies.
$source=[regex]::Replace($source,'(?m)^using [A-Za-z0-9_.]+;\r?$','')
$source='using System; using System.IO; using System.ComponentModel; using System.Runtime.InteropServices; using System.Threading; using System.Collections.Generic;'+[Environment]::NewLine+$source
Add-Type -TypeDefinition ($source+[Environment]::NewLine+$test)
[EcResearch.IteControlTests]::Run()
'PASS: full-speed staging, exact write allowlist, unrelated flag preservation, auto restoration, identity/version/permission/config guards, external-controller refusal.'
