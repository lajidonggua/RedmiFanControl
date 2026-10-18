using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;

namespace EcResearch {
    // Compiled alone, this file exposes reads only. The separate fan-control
    // partial explicitly opts in to writes of three allowlisted control bytes.
    public sealed partial class StrictSuperIoProbe : IDisposable {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern IntPtr LoadLibraryEx(string name, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true, SetLastError=true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OpenFn(out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LoadFn(IntPtr handle,byte[] blob,UIntPtr size);
        [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Ansi)]
        private delegate int ExecuteFn(IntPtr handle,[MarshalAs(UnmanagedType.LPStr)] string name,ulong[] input,UIntPtr inputCount,[Out] ulong[] output,UIntPtr outputCount,out UIntPtr returned);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CloseFn(IntPtr handle);
        private IntPtr handle;
        private ExecuteFn execute;
        private CloseFn close;
        private Mutex mutex;
        private Func<string,ulong[],int,ulong[]> testTransport;
        private ushort indexPort=0x2E;
        private bool amdTemperatureModule;
        private static T Bind<T>(IntPtr library,string name) where T:class {
            var ptr=GetProcAddress(library,name);
            if(ptr==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),name);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(ptr,typeof(T));
        }
        private static void Check(int status,string operation) {
            if(status<0)throw new InvalidOperationException(operation+" NTSTATUS=0x"+unchecked((uint)status).ToString("X8"));
        }
        public StrictSuperIoProbe(string libraryPath,string modulePath) {
            amdTemperatureModule=String.Equals(Path.GetFileName(modulePath),"AMDFamily17.bin",StringComparison.OrdinalIgnoreCase);
            if(!Environment.Is64BitProcess)throw new InvalidOperationException("64-bit process required.");
            var lib=LoadLibraryEx(Path.GetFullPath(libraryPath),IntPtr.Zero,0x100|0x800);
            if(lib==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"LoadLibraryEx");
            var open=Bind<OpenFn>(lib,"pawnio_open_nt");
            var load=Bind<LoadFn>(lib,"pawnio_load_nt");
            execute=Bind<ExecuteFn>(lib,"pawnio_execute_nt");
            close=Bind<CloseFn>(lib,"pawnio_close_nt");
            Check(open(out handle),"pawnio_open");
            try {
                var blob=File.ReadAllBytes(modulePath);
                Check(load(handle,blob,(UIntPtr)blob.Length),"pawnio_load_"+Path.GetFileNameWithoutExtension(modulePath));
                mutex=new Mutex(false,"Global\\Access_ISABUS.HTP.Method");
            }catch{close(handle);handle=IntPtr.Zero;throw;}
        }
        // Internal test-only transport: never opens a native driver handle.
        internal StrictSuperIoProbe(Func<string,ulong[],int,ulong[]> transport) {
            testTransport=transport;mutex=new Mutex(false);
        }
        private ulong[] Call(string name,ulong[] input,int count) {
            if(testTransport!=null)return testTransport(name,input,count);
            var output=new ulong[count];UIntPtr returned;
            Check(execute(handle,name,input,(UIntPtr)input.Length,output,(UIntPtr)count,out returned),name);
            if(returned.ToUInt64()!=(ulong)count)throw new InvalidOperationException("Invalid PawnIO result count.");
            return output;
        }
        // Fixed, read-only temperature register; no SMN/MSR value writes exposed.
        public double ReadCpuTemperature() {
            if(!amdTemperatureModule)throw new InvalidOperationException("AMD temperature module required.");
            using(var pci=new Mutex(false,"Global\\Access_PCI")) {
                bool locked=false;
                try {
                    try{locked=pci.WaitOne(1000);}catch(AbandonedMutexException){locked=true;}
                    if(!locked)throw new TimeoutException("PCI mutex busy; thermal reading unavailable.");
                    ulong raw=Call("ioctl_read_smn",new ulong[]{0x00059800},1)[0];
                    if(raw==0 || raw>=UInt32.MaxValue)throw new InvalidOperationException("Invalid AMD thermal register.");
                    return DecodeCpuTemperature((uint)raw);
                }finally{if(locked)pci.ReleaseMutex();}
            }
        }
        public static double DecodeCpuTemperature(uint raw) {
            double value=(raw>>21)*0.125-((raw&0x80000)!=0 ? 49.0 : 0.0);
            if(value<5 || value>115)throw new InvalidOperationException("Implausible CPU temperature; load prohibited.");
            return value;
        }
        private void Key(byte value) {
            // Only the two standard configuration index ports, never arbitrary ports.
            if(indexPort!=0x2E && indexPort!=0x4E)throw new InvalidOperationException("SIO port allowlist violation.");
            Call("ioctl_pio_outb",new ulong[]{indexPort,value},0);
        }
        private byte Read(byte index) {
            var value=Call("ioctl_superio_inb",new ulong[]{index},1)[0];
            if(value>255)throw new InvalidOperationException("Invalid SIO byte.");
            return (byte)value;
        }
        private void Select(byte ldn) {
            // Register 07 selects a logical device; do not change its configuration.
            Call("ioctl_superio_outb",new ulong[]{0x07,ldn},0);
            if(Read(0x07)!=ldn)throw new InvalidOperationException("LDN selection did not echo.");
        }
        public sealed class LogicalDevice {
            public int Ldn,Active,DataBase,CommandBase,Irq;
        }
        public sealed class Snapshot {
            public int ChipId,RepeatedChipId,Revision,OriginalLdn,Slot,IndexPort;
            public bool RecognizedIte;
            public LogicalDevice[] Devices;
        }
        public Snapshot ReadConfiguration() {
            return ReadConfiguration(0);
        }
        public Snapshot ReadConfiguration(int slot) {
            if(slot!=0 && slot!=1)throw new ArgumentOutOfRangeException("slot");
            bool locked=false,entered=false,selected=false;
            byte original=0;
            try {
                try{locked=mutex.WaitOne(1000);}catch(AbandonedMutexException){locked=true;}
                if(!locked)throw new TimeoutException("ISA bus mutex busy.");
                indexPort=(ushort)(slot==0 ? 0x2E : 0x4E);
                Call("ioctl_select_slot",new ulong[]{(ulong)slot},0);
                // Standard ITE enter/exit sequence, no configuration values changed.
                Key(0x87);Key(0x01);Key(0x55);Key((byte)(slot==0 ? 0x55 : 0xAA));entered=true;
                var result=new Snapshot();
                result.Slot=slot;result.IndexPort=indexPort;
                result.ChipId=(Read(0x20)<<8)|Read(0x21);
                result.RepeatedChipId=(Read(0x20)<<8)|Read(0x21);
                result.Revision=Read(0x22);
                if(result.ChipId!=result.RepeatedChipId)throw new InvalidOperationException("Unstable SIO identification.");
                int[] known={0x5570,0x5571,0x5573,0x5576,0x8510,0x8511,0x8512,0x8516,0x8518,0x8528,0x8580,0x8586,0x8587,0x8225,0x8227,0x8231};
                result.RecognizedIte=Array.IndexOf(known,result.ChipId)>=0;
                var devices=new List<LogicalDevice>();
                if(result.RecognizedIte) {
                    original=Read(0x07);result.OriginalLdn=original;selected=true;
                    // Firmware's EC2I table uses PMC1 11 and PMC2 12.
                    foreach(byte ldn in new byte[]{0x11,0x12}) {
                        Select(ldn);
                        devices.Add(new LogicalDevice{Ldn=ldn,Active=Read(0x30),
                            DataBase=(Read(0x60)<<8)|Read(0x61),
                            CommandBase=(Read(0x62)<<8)|Read(0x63),Irq=Read(0x70)});
                    }
                }
                result.Devices=devices.ToArray();return result;
            }finally{
                try{if(selected)Select(original);}
                finally{
                    try{if(entered){Key(0x02);Call("ioctl_pio_outb",new ulong[]{(ulong)(indexPort+1),0x02},0);}}
                    finally{if(locked)mutex.ReleaseMutex();}
                }
            }
        }
        // Depth-2 I2EC access: only address latches may be written. Subregister
        // 12 (EC data) is exclusively read. No arbitrary address/write API.
        private void DepthSelect(byte subregister) {
            if(subregister!=0x10 && subregister!=0x11 && subregister!=0x12)
                throw new InvalidOperationException("Depth-2 selector allowlist violation.");
            Call("ioctl_superio_outb",new ulong[]{0x2E,subregister},0);
        }
        private byte DepthRead(byte subregister) {
            DepthSelect(subregister);return Read(0x2F);
        }
        private void AddressLatch(byte subregister,byte value) {
            if(subregister!=0x10 && subregister!=0x11)
                throw new InvalidOperationException("EC data writes are prohibited.");
            DepthSelect(subregister);
            Call("ioctl_superio_outb",new ulong[]{0x2F,value},0);
        }
        private byte FanRamByte(ushort address) {
            ushort[] allowed={0x0100,0x0105,0x0106,0x0298,0x0299,0x02B8,0x02B9,0x0400,0x0401,0x0402,0x0407,0x0408,0x0490,0x0491,0x0492,
                0x2000,0x2001,0x2002,0x200D,0x1804,0x1805,0x185C,0x185D,0x1841,0x185B,0x180A,0x180F};
            if(Array.IndexOf(allowed,address)<0)throw new InvalidOperationException("SRAM read allowlist violation.");
            AddressLatch(0x11,(byte)(address>>8));AddressLatch(0x10,(byte)(address&255));
            if(DepthRead(0x11)!=(byte)(address>>8) || DepthRead(0x10)!=(byte)(address&255))
                throw new InvalidOperationException("SRAM address latch did not echo.");
            return DepthRead(0x12);
        }
        private int StableFanWord(ushort high) {
            for(int attempt=0;attempt<3;attempt++) {
                int first=FanRamByte(high),low=FanRamByte((ushort)(high+1)),last=FanRamByte(high);
                if(first==last)return (first<<8)|low;
            }
            throw new InvalidOperationException("Unstable RPM word.");
        }
        public sealed class FanRamSnapshot {
            public int ChipId,Fan1Word,Fan2Word,BatteryMillivolts,BatteryPercent;
            public int Tsr0Raw,Tsr1Raw;
            public string EcVersionHex;
            public Dictionary<string,int> ReadOnlyConfiguration;
            public string Transport="ITE SIO depth-2 I2EC read";
        }
        public FanRamSnapshot ReadFanRam() {
            return ReadFanRamCore(false);
        }
        public FanRamSnapshot ReadFanDiagnostics() {
            return ReadFanRamCore(true);
        }
        private FanRamSnapshot ReadFanRamCore(bool diagnostics) {
            bool locked=false,entered=false,saved=false;
            byte originalSelector=0,originalHigh=0,originalLow=0;
            try {
                try{locked=mutex.WaitOne(1000);}catch(AbandonedMutexException){locked=true;}
                if(!locked)throw new TimeoutException("ISA bus mutex busy.");
                indexPort=0x4E;Call("ioctl_select_slot",new ulong[]{1},0);
                Key(0x87);Key(0x01);Key(0x55);Key(0xAA);entered=true;
                int id=(Read(0x20)<<8)|Read(0x21);
                if(id!=0x5571 || ((Read(0x20)<<8)|Read(0x21))!=id)
                    throw new InvalidOperationException("Expected stable IT5571 identity.");
                originalSelector=Read(0x2E);
                originalHigh=DepthRead(0x11);originalLow=DepthRead(0x10);saved=true;
                var result=new FanRamSnapshot{ChipId=id};
                result.EcVersionHex=FanRamByte(0x0400).ToString("X2")+FanRamByte(0x0401).ToString("X2")+FanRamByte(0x0402).ToString("X2");
                if(result.EcVersionHex!="500111")throw new InvalidOperationException("Live EC version differs from analysed P.11 image: "+result.EcVersionHex);
                result.Fan1Word=StableFanWord(0x0298);
                result.Fan2Word=StableFanWord(0x02B8);
                result.BatteryMillivolts=FanRamByte(0x0490)|(FanRamByte(0x0491)<<8);
                result.BatteryPercent=FanRamByte(0x0492);
                result.Tsr0Raw=FanRamByte(0x0407);result.Tsr1Raw=FanRamByte(0x0408);
                if(diagnostics) {
                    result.ReadOnlyConfiguration=new Dictionary<string,int>();
                    foreach(ushort address in new ushort[]{0x2000,0x2001,0x2002,0x200D,0x1804,0x1805,0x185C,0x185D,0x1841,0x185B,0x180A,0x180F})
                        result.ReadOnlyConfiguration.Add(address.ToString("X4"),FanRamByte(address));
                    if(((result.ReadOnlyConfiguration["2000"]<<8)|result.ReadOnlyConfiguration["2001"])!=id)
                        throw new InvalidOperationException("EC address-space identity mismatch.");
                    if((result.ReadOnlyConfiguration["200D"]&2)==0)
                        throw new InvalidOperationException("I2EC read permission not reported; no permissions will be changed.");
                }
                return result;
            }finally {
                try {
                    if(saved) {
                        AddressLatch(0x11,originalHigh);AddressLatch(0x10,originalLow);
                        // Restore selector only; never write EC data or configuration.
                        Call("ioctl_superio_outb",new ulong[]{0x2E,originalSelector},0);
                    }
                }finally {
                    try{if(entered){Key(0x02);Call("ioctl_pio_outb",new ulong[]{0x4F,0x02},0);}}
                    finally{if(locked)mutex.ReleaseMutex();}
                }
            }
        }
        public void Dispose(){if(handle!=IntPtr.Zero){close(handle);handle=IntPtr.Zero;}if(mutex!=null)mutex.Dispose();}
    }
}
