$ErrorActionPreference='Stop'
$source=Get-Content (Join-Path $PSScriptRoot '..\tools\StrictSuperIoProbe.cs') -Raw
$test=@'
namespace EcResearch {
    public static class IteReadOnlyTests {
        sealed class Fake {
            public byte selector=0x44,high=0x03,low=0x45;
            public bool exited=false,fail=false,badIdentity=false,badLatch=false,badVersion=false;
            public int reads=0;
            public ulong[] Invoke(string name,ulong[] input,int count) {
                if(name=="ioctl_select_slot") {if(input[0]!=1)throw new Exception("Wrong slot");return new ulong[0];}
                if(name=="ioctl_pio_outb") {
                    if(input[0]==0x4F && input[1]==2){exited=true;return new ulong[0];}
                    if(input[0]!=0x4E || Array.IndexOf(new ulong[]{0x87,1,0x55,0xAA,2},input[1])<0)throw new Exception("Unexpected port write");
                    return new ulong[0];
                }
                if(name=="ioctl_superio_outb") {
                    if(input[0]==0x2E){selector=(byte)input[1];return new ulong[0];}
                    if(input[0]!=0x2F || (selector!=0x10 && selector!=0x11))throw new Exception("Forbidden data/config write");
                    if(selector==0x10)low=(byte)input[1];else high=(byte)input[1];
                    return new ulong[0];
                }
                if(name!="ioctl_superio_inb")throw new Exception("Unexpected operation");
                byte value=0;
                switch(input[0]) {
                    case 0x20:value=badIdentity?(byte)0:(byte)0x55;break;
                    case 0x21:value=0x71;break;
                    case 0x2E:value=selector;break;
                    case 0x2F:
                        if(selector==0x10)value=badLatch?(byte)0xFF:low;
                        else if(selector==0x11)value=high;
                        else if(selector==0x12) {
                            reads++;
                            if(fail)throw new Exception("Injected read failure");
                            int address=(high<<8)|low;
                            switch(address){
                                case 0x0298:value=0x0C;break;case 0x0299:value=0x80;break;
                                case 0x02B8:value=0x0B;break;case 0x02B9:value=0xB8;break;
                                case 0x0490:value=0x80;break;case 0x0491:value=0x3E;break;
                                case 0x0492:value=76;break;
                                case 0x0407:value=47;break;case 0x0408:value=40;break;
                                case 0x0400:value=0x50;break;case 0x0401:value=1;break;case 0x0402:value=badVersion?(byte)0:(byte)0x11;break;
                                case 0x2000:value=0x55;break;case 0x2001:value=0x71;break;case 0x2002:value=2;break;case 0x200D:value=0xC3;break;
                                case 0x1804:case 0x1805:case 0x185C:case 0x185D:case 0x185B:case 0x180A:case 0x180F:value=0;break;
                                case 0x1841:value=127;break;
                                default:throw new Exception("Unexpected address");
                            }
                        } else throw new Exception("Unexpected subregister read");
                        break;
                    default:throw new Exception("Unexpected register read");
                }
                return new ulong[]{value};
            }
        }
        public static void Run() {
            var f=new Fake();
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                var result=p.ReadFanRam();
                if(result.Fan1Word!=3200 || result.Fan2Word!=3000 || result.BatteryMillivolts!=16000 || result.BatteryPercent!=76)throw new Exception("Decode mismatch");
            }
            if(!f.exited || f.high!=3 || f.low!=0x45 || f.selector!=0x44)throw new Exception("Address restoration failed");
            f=new Fake();f.fail=true;
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                try{p.ReadFanRam();throw new Exception("Failure swallowed");}catch(Exception ex){if(ex.Message!="Injected read failure")throw;}
            }
            if(!f.exited || f.high!=3 || f.low!=0x45 || f.selector!=0x44)throw new Exception("Error cleanup failed");
            f=new Fake();f.badIdentity=true;
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                try{p.ReadFanRam();throw new Exception("Identity failure swallowed");}catch(InvalidOperationException){}
            }
            if(f.reads!=0 || !f.exited)throw new Exception("Identity guard failed");
            f=new Fake();f.badLatch=true;
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                try{p.ReadFanRam();throw new Exception("Address echo failure swallowed");}catch(InvalidOperationException){}
            }
            if(f.reads!=0 || !f.exited)throw new Exception("Address guard failed");
            f=new Fake();f.badVersion=true;
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                try{p.ReadFanRam();throw new Exception("Version failure swallowed");}catch(InvalidOperationException){}
            }
            if(f.reads!=3 || !f.exited)throw new Exception("Version guard failed");
            f=new Fake();
            using(var p=new StrictSuperIoProbe(f.Invoke)) {
                var result=p.ReadFanDiagnostics();
                if(result.ReadOnlyConfiguration["200D"]!=0xC3 || result.ReadOnlyConfiguration["1841"]!=127)throw new Exception("Diagnostic decode mismatch");
            }
            if(!f.exited || f.high!=3 || f.low!=0x45 || f.selector!=0x44)throw new Exception("Diagnostic restoration failed");
        }
    }
}
'@
Add-Type -TypeDefinition ($source+[Environment]::NewLine+$test)
[EcResearch.IteReadOnlyTests]::Run()
'PASS: fixed-address read protocol, byte order, prohibited writes, address restoration, failure cleanup, identity guard.'
