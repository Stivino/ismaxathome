/*
 * MaxGate V1.0
 * © Steven Weber 2022
 * 
 * # Summary 
 * This program uses a ADXL345 acceleration sensor to determine the state of Max' cat door.
 * 
 * # Calibration
 * The program starts with a calibration, where the three states of the cat flap (closed, inside, outside) are
 * getting referenced to default values.
 *
 * # Register as a service app on pi
 * https://www.nikouusitalo.com/blog/creating-an-autostart-net-6-service-on-a-raspberry-pi/
 * 
 * # Copy to PI [CMD]
 * C:\GitHub\MaxGate\MaxGate\bin\Release\net6.0\publish>scp -r * pi@raspberrypi:/home/pi/MaxGate/
 *
 * # Start on Pi [Putty]
 * dotnet MaxGate.dll
*/

using Iot.Device.Adxl345;
using Iot.Device.Nmea0183;
using Mastonet;
using Mastonet.Entities;
using System.Device.Gpio;
using System.Device.Spi;
using System.Diagnostics;
using System.Numerics;
using System.Text;


namespace MaxGate // Note: actual namespace depends on the project name.
{

    public enum ConsoleLevels { Info, Warning, Error, Debug };
    public enum FlapStates { Closed, Outside, Inside };
    internal class Program
    {
        static string StatesFile = "states.max";
        private Vector3 v = new Vector3();


        static void Main(string[] args)
        {
            // See https://aka.ms/new-console-template for more information
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" |\\---/| ");
            Console.WriteLine(" | o_o | ");
            Console.WriteLine("  \\_v_ / ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("MaxGate V1.2");
            Console.WriteLine("^-d -debug Print all measuringpoints");
            Console.WriteLine("^-c -calibration Redo calibration of sensor");
            Console.WriteLine("+~+~+~+~+~+~+~+~+~+~+~+~+~+~+~+~+~+~");
            // init Mastodon client
            var instance = "botsin.space";
            var authClient = new AuthenticationClient(instance);
            var appRegistration = authClient.CreateApp("Is Max at home?", Scope.Read | Scope.Write | Scope.Follow);
            var accessToken = File.ReadAllText("secret.txt").Trim();
            var client = new MastodonClient(instance, accessToken);

            bool trace = false;
            bool useFile = true;
            List<string> argsList = args.ToList();
            if (argsList.Contains("-debug") || argsList.Contains("-d"))
            {
                trace = true;
            }
            if (argsList.Contains("-calibration") || argsList.Contains("-c"))
            {
                useFile = false;
            }

            Vector3 closed = new Vector3();
            Vector3 openIN = new Vector3(); ;
            Vector3 openOUT = new Vector3(); ;
            SpiConnectionSettings settings = new SpiConnectionSettings(0, 0)
            {
                ClockFrequency = Adxl345.SpiClockFrequency,
                Mode = Adxl345.SpiMode
            };
            var adx345Sensor = SpiDevice.Create(settings);

            if (useFile)
            {
                InitializeFromFile(ref closed, ref openIN, ref openOUT);
            }
            else
            {

                DoCalibration(out closed, out openIN, out openOUT, adx345Sensor);
            }
            Console.WriteLine(("Started."));
            // Set gravity measurement range ±4G
            using (Adxl345 sensor = new Adxl345(adx345Sensor, GravityRange.Range04))
            {
                FlapStates current = FlapStates.Closed;
                FlapStates last = FlapStates.Closed; ;
                // loop
                while (true)
                {
                    Vector3 v = GetFlapVector(adx345Sensor);   //PrintConsole($"X:{openIN.X.ToString("0.00")} Y:{openIN.Y.ToString("0.00")} Z:{openIN.Z.ToString("0.00")}g", ConsoleLevels.Debug);
                    current = GetFlapState(v, closed, openIN, openOUT);
                    if (trace)
                    {
                        Console.WriteLine(String.Format("{0,7} {1,5} {2,5} {3,5}", "", v.X.ToString("0.00"), v.Y.ToString("0.00"), v.Z.ToString("0.00")));
                    }
                    if ((current != FlapStates.Closed) && (current != last))
                    {
                        Toot(client, current);
                        Console.WriteLine(String.Format("{0,7} {1,5} {2,5} {3,5}", Enum.GetName(typeof(FlapStates), current), v.X.ToString("0.00"), v.Y.ToString("0.00"), v.Z.ToString("0.00")));
                        Console.WriteLine("Stopping measuring for 15 seconds.");
                        for (int i = 15; i > 0; i--)
                        {
                            Console.WriteLine((i + "..."));
                            Thread.Sleep(1000);
                        }
                    }
                    last = current;
                }
            }
        }

        private static void DoCalibration(out Vector3 closed, out Vector3 openIN, out Vector3 openOUT, SpiDevice device)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            PrintConsole("Press any key to start calibration", ConsoleLevels.Info);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.ReadKey();
            PrintConsole("Start calibration...", ConsoleLevels.Info);
            PrintConsole("Read values for state [closed]...", ConsoleLevels.Info);
            PrintConsole("Done.", ConsoleLevels.Info);
            closed = GetFlapVector(device);
            PrintConsole($"[CLOSED] X:{closed.X.ToString("0.00")} Y:{closed.Y.ToString("0.00")} Z:{closed.Z.ToString("0.00")}g", ConsoleLevels.Info);
            PrintConsole("Open flap to OUTside and wait...", ConsoleLevels.Info);
            for (int i = 4; i > 0; i--)
            {
                Console.WriteLine((i + "..."));
                Thread.Sleep(1000);
            }
            openOUT = GetFlapVector(device);
            PrintConsole($"[OUT] X:{openOUT.X.ToString("0.00")} Y:{openOUT.Y.ToString("0.00")} Z:{openOUT.Z.ToString("0.00")}g", ConsoleLevels.Info);
            PrintConsole("Done.", ConsoleLevels.Info);
            PrintConsole("Open flap to INside and wait...", ConsoleLevels.Info);
            for (int i = 4; i > 0; i--)
            {
                Console.WriteLine((i + "..."));
                Thread.Sleep(1000);
            }
            openIN = GetFlapVector(device);
            PrintConsole($"[IN] X:{openIN.X.ToString("0.00")} Y:{openIN.Y.ToString("0.00")} Z:{openIN.Z.ToString("0.00")}g", ConsoleLevels.Info);
            PrintConsole("Done.", ConsoleLevels.Info);
            PrintConsole("Calibration completed. The Gate is ready.", ConsoleLevels.Info);
            List<string> data = new List<string>();
            data.Add(String.Format("C;{0};{1};{2}", closed.X, closed.Y, closed.Z));
            data.Add(String.Format("O;{0};{1};{2}", openOUT.X, openOUT.Y, openOUT.Z));
            data.Add(String.Format("I;{0};{1};{2}", openIN.X, openIN.Y, openIN.Z));
            File.WriteAllLines(StatesFile, data);
            Console.WriteLine();
            Console.WriteLine(String.Format("{0,7} {1,5} {2,5} {3,5}", "State", "X (g)", "Y (g)", "Z (g)"));
            for (int i = 4; i > 0; i--)
            {
                Console.WriteLine((i + " second/s until starting..."));
                Thread.Sleep(1000);
            }
        }

        private static void InitializeFromFile(ref Vector3 closed, ref Vector3 openIN, ref Vector3 openOUT)
        {
            List<string> data = File.ReadAllLines(StatesFile).ToList();
            Console.WriteLine(String.Format("Init file has {0} lines", data.Count));
            foreach (var item in data)
            {
                string[] segs = item.Split(";");
                float x = float.Parse(segs[1]);
                float y = float.Parse(segs[2]);
                float z = float.Parse(segs[3]);
                switch (segs[0])
                {
                    case "O":
                        openOUT = new Vector3(x, y, z);
                        Console.WriteLine(String.Format("File-OUT: {0};{1};{2};", x, y, z));
                        break;
                    case "I":
                        openIN = new Vector3(x, y, z);
                        Console.WriteLine(String.Format("File-IN: {0};{1};{2};", x, y, z));
                        break;
                    case "C":
                        closed = new Vector3(x, y, z); 
                        Console.WriteLine(String.Format("File-CLOSED: {0};{1};{2};", x, y, z));
                        break;
                    default:
                        break;
                }
            }
        }

        static FlapStates GetFlapState(Vector3 measurement, Vector3 closed, Vector3 openIN, Vector3 openOUT)
        {
            closed = Vector3.Subtract(measurement, closed);
            openIN = Vector3.Subtract(measurement, openIN);
            openOUT = Vector3.Subtract(measurement, openOUT);
            FlapStates flap = (closed.Length() < openIN.Length()) ? FlapStates.Closed : FlapStates.Inside;
            if (flap == FlapStates.Closed)
                flap = (closed.Length() < openOUT.Length()) ? FlapStates.Closed : FlapStates.Outside;
            else
                flap = (openIN.Length() < openOUT.Length()) ? FlapStates.Inside : FlapStates.Outside;
            return flap;
        }

        /// <summary>
        /// Returns current movement vector from 3 measurements
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        static Vector3 GetFlapVector(SpiDevice device)
        {
            Vector3 data = new Vector3(0, 0, 0);
            using (Adxl345 sensor = new Adxl345(device, GravityRange.Range04))
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 tmp = sensor.Acceleration;
                    data += tmp;
                    Thread.Sleep(200);
                }
            }
            data = data / new Vector3(3, 3, 3);
            return data;
        }
        static void PrintConsole(string text, ConsoleLevels level)
        {
            ConsoleColor c = ConsoleColor.Green;
            StringBuilder s = new StringBuilder();
            switch (level)
            {
                case ConsoleLevels.Info:
                    s.Append("[INFO]");
                    c = ConsoleColor.Green;
                    break;
                case ConsoleLevels.Warning:
                    s.Append("[WARN]");
                    c = ConsoleColor.Yellow;
                    break;
                case ConsoleLevels.Error:
                    s.Append("[ERROR]");
                    c = ConsoleColor.Red;
                    break;
                case ConsoleLevels.Debug:
                    s.Append("[DEBUG]");
                    c = ConsoleColor.Magenta;
                    break;
                default:
                    break;
            }
            s.Append(" ");
            Console.ForegroundColor = c;
            Console.Write(s);
            Console.ResetColor();
            Console.Write(text);
            Console.WriteLine();
        }


        static void Toot(MastodonClient client, FlapStates state)
        {
            try
            {
                switch (state)
                {
                    case FlapStates.Outside:
                        client.PublishStatus("Max left at " + DateTime.Now.ToString("HH:mm:ss"), Visibility.Public);
                        break;
                    case FlapStates.Inside:
                        client.PublishStatus("Max has been home since " + DateTime.Now.ToString("HH:mm:ss"), Visibility.Public);
                        break;
                    case FlapStates.Closed:
                    default:
                        break;
                }
            }


            catch (Exception ex)
            {
                Console.Write(ex);
                Console.WriteLine();
            }
        }
    }
}