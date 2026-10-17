using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Project_Nested.Injection;

namespace Project_Nested
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length >= 2)
            {
                RunCli(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        static void SetBool(Injector injector, string name, bool value)
        {
            injector.SetSetting(name, value ? "true" : "false");
        }

        static void ApplyCompatibilityProfile(Injector injector, string profile)
        {
            switch (profile)
            {
                case "default":
                    break;
                case "zero-memory":
                    SetBool(injector, "MemoryEmulation.ZeroMemoryReset", true);
                    break;
                case "zero-memory-accurate-stack":
                    SetBool(injector, "MemoryEmulation.ZeroMemoryReset", true);
                    SetBool(injector, "StackEmulation.NativeReturn", false);
                    SetBool(injector, "StackEmulation.NativeReturnInterrupt", false);
                    SetBool(injector, "StackEmulation.LazyDoubleReturn", false);
                    SetBool(injector, "StackEmulation.StackUnderflow", true);
                    SetBool(injector, "StackEmulation.SafeTsx", true);
                    break;
                case "no-native-irq":
                    SetBool(injector, "StackEmulation.NativeReturnInterrupt", false);
                    break;
                case "no-native-return":
                    SetBool(injector, "StackEmulation.NativeReturn", false);
                    break;
                case "no-native-both":
                    SetBool(injector, "StackEmulation.NativeReturn", false);
                    SetBool(injector, "StackEmulation.NativeReturnInterrupt", false);
                    break;
                case "nmi-vblank":
                    SetBool(injector, "NmiMode.AtVblank", true);
                    SetBool(injector, "NmiMode.AutoDetect", false);
                    break;
                case "nmi-vblank-no-native-irq":
                    SetBool(injector, "NmiMode.AtVblank", true);
                    SetBool(injector, "NmiMode.AutoDetect", false);
                    SetBool(injector, "StackEmulation.NativeReturnInterrupt", false);
                    break;
                case "nmi-auto":
                    SetBool(injector, "NmiMode.AtVblank", false);
                    SetBool(injector, "NmiMode.AutoDetect", true);
                    break;
                case "page09":
                    SetBool(injector, "StackEmulation.Page01", false);
                    break;
                case "accurate-stack":
                    SetBool(injector, "StackEmulation.NativeReturn", false);
                    SetBool(injector, "StackEmulation.NativeReturnInterrupt", false);
                    SetBool(injector, "StackEmulation.LazyDoubleReturn", false);
                    SetBool(injector, "StackEmulation.StackUnderflow", true);
                    SetBool(injector, "StackEmulation.SafeTsx", true);
                    break;
                case "ppu-sync-auto":
                    SetBool(injector, "NmiMode.AtVblank", false);
                    SetBool(injector, "NmiMode.AutoDetect", true);
                    SetBool(injector, "SyncPpuStatusToSnes", true);
                    break;
                default:
                    throw new ArgumentException("Unknown compatibility profile: " + profile);
            }
        }

        static void ApplyCallSeed(Injector injector, string callSeedArg)
        {
            if (String.IsNullOrWhiteSpace(callSeedArg) || callSeedArg == "-")
                return;

            string seed = File.Exists(callSeedArg)
                ? File.ReadAllText(callSeedArg)
                : callSeedArg;
            seed = seed.Replace("\r", " ").Replace("\n", " ").Trim();
            if (seed.StartsWith("Calls:", StringComparison.OrdinalIgnoreCase))
                injector.SetSetting(seed);
            else
                injector.SetSetting("Calls: " + seed);
        }

        static void ApplyInputMap(Injector injector, int[] map)
        {
            injector.SetSetting("Input.Enabled", "true");
            for (int i = 0; i < map.Length; i++)
                injector.SetSetting("Input.Map", i, map[i].ToString());
        }

        static void RunCli(string[] args)
        {
            string input = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            string gameName = args.Length >= 3 ? args[2] : "Karate Champ Rev A";
            string emulatorName = args.Length >= 4 ? args[3] : "Karate Champ MSU";
            string inputProfile = args.Length >= 5 ? args[4].Trim().ToLowerInvariant() : "macro";
            bool optimize = args.Length >= 6 && args[5].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            string compatProfile = args.Length >= 7 ? args[6].Trim().ToLowerInvariant() : "default";
            string callSeed = args.Length >= 8 ? args[7] : null;

            if (!File.Exists(input))
                throw new FileNotFoundException("NES input not found", input);
            if (inputProfile != "stock" && inputProfile != "macro" && inputProfile != "twin" && inputProfile != "modern")
                throw new ArgumentException("inputProfile must be stock, macro, twin, or modern");

            var injector = new Injector(File.ReadAllBytes(input));
            if (!injector.IsLoaded(false))
                throw new InvalidOperationException("Project_Nested.smc could not be loaded beside the executable.");
            if (!injector.mapperSupported)
                throw new InvalidOperationException("Mapper is not supported by this Project Nested build.");

            injector.GameName.SetValue(gameName);
            injector.EmulatorName.SetValue(emulatorName);
            injector.SetSetting("Optimize.Enabled", optimize ? "true" : "false");
            ApplyCompatibilityProfile(injector, compatProfile);
            ApplyCallSeed(injector, callSeed);

            if (inputProfile == "macro" || inputProfile == "modern")
            {
                ApplyInputMap(injector, new int[] { 1, 9, 3, 4, 5, 6, 7, 8, 2, 10, 11, 12 });
            }
            else if (inputProfile == "twin")
            {
                ApplyInputMap(injector, new int[] { 12, 9, 3, 4, 5, 6, 7, 8, 11, 10, 0, 0 });
            }
            else
            {
                injector.SetSetting("Input.Enabled", "false");
            }

            CancellationToken? token = new CancellationToken?(CancellationToken.None);
            byte[] data = injector.FinalChanges(token, null).GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, data);

            int[] known = injector.GetAllKnownCalls();
            Console.WriteLine("Wrote: " + output);
            Console.WriteLine("Bytes: " + data.Length);
            Console.WriteLine("Known calls: " + known.Length);
            Console.WriteLine("Known call list: " + String.Join(",", known.OrderBy(x => x).Select(x => "0x" + x.ToString("x6"))));
            Console.WriteLine("Input profile: " + inputProfile);
            Console.WriteLine("Optimize.Enabled: " + optimize);
            Console.WriteLine("Compatibility profile: " + compatProfile);
            Console.WriteLine("Call seed: " + (String.IsNullOrWhiteSpace(callSeed) ? "none" : callSeed));
            if (inputProfile == "macro" || inputProfile == "modern")
                Console.WriteLine("Input.Map: 1,9,3,4,5,6,7,8,2,10,11,12");
            else if (inputProfile == "twin")
                Console.WriteLine("Input.Map: 12,9,3,4,5,6,7,8,11,10,0,0");
        }
    }
}
