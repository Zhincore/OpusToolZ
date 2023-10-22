using System;
using System.IO;
using System.Linq;
using WolvenKit.Modkit.RED4.Opus;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpusToolZ
{
    class Program
    {
        static int Main(string[] args)
        {
            string command = args[0];
            string opusinfo = args[1].Replace("\"", string.Empty);

            // Load opusinfo
            FileStream fs = new FileStream(opusinfo, FileMode.Open, FileAccess.Read);
            BinaryReader br = new BinaryReader(fs);
            fs.Position = 0;
            if (BitConverter.ToString(br.ReadBytes(3)) != "53-4E-44")
            {
                fs.Dispose();
                fs.Close();
                Console.WriteLine("Not a opusinfo file");
                return 1;
            }

            var info = new OpusInfo(fs);
            
            List<UInt16> tempolisto = new List<UInt16>();
            for (int i = 0; i < info.OpusCount; i++)
            {
                if (!tempolisto.Contains(info.PackIndices[i]))
                {
                    tempolisto.Add(info.PackIndices[i]);
                }
            }

            UInt32 numOfPaks = Convert.ToUInt32(tempolisto.Count);

            Console.WriteLine("Found " + numOfPaks + " paks and " + info.OpusCount + " opuses");

            // Prepare opuspak streams
            string[] files = Directory.GetFiles(Path.GetDirectoryName(opusinfo), "*.opuspak").OrderBy(_ => Convert.ToUInt32(_.Replace(".opuspak", string.Empty).Substring(_.LastIndexOf('_') + 1))).ToArray();
            Stream[] streams = new Stream[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                Console.WriteLine("Loading pak " + (i + 1) + "/" + files.Length);
                streams[i] = new FileStream(files[i], FileMode.Open, FileAccess.Read);
            }

            if (files.Length != numOfPaks)
            {
                Console.WriteLine("Not all of " + Convert.ToString(numOfPaks) + " .opuspak files are present in the inputDir of sfx_container.opusinfo");
                return 1;
            }

            switch (command) {
                case "extract":
                    return Extract(args, info, streams);
                case "repack":
                    return Repack(args, info, streams);
                default:
                    Console.WriteLine("Unknown command");
                    return 1;
            }

        }

        static int Extract(string[] args, OpusInfo info, Stream[] streams) {
            string dir = args[2].Replace("\"", string.Empty);

            if (!Directory.Exists(dir))
            {
                Console.WriteLine("Invalid output inputDir! create it");
                return 1;
            }

            string json = JsonConvert.SerializeObject(info);
            File.WriteAllText(dir + "\\info.json", json);

            info.WriteAllOpusFromPaks(streams, new DirectoryInfo(dir));
            Console.WriteLine("output: " + dir);

            return 0;
        }

        static int Repack(string[] args, OpusInfo info, Stream[] streams) {
            string inputDir = args[2].Replace("\"", string.Empty);
            string outputDir = args[3].Replace("\"", string.Empty);

            // Scan inputDir for wavs
            List<UInt32> foundids = new List<UInt32>();
            foreach (string file in Directory.GetFiles(inputDir, "*.wav")  ) {
                try {
                    UInt32 id = Convert.ToUInt32(Path.GetFileNameWithoutExtension(file));

                    bool found = false;
                    for (int i = 0; i < info.OpusCount; i++)
                    {
                        if (info.OpusHashes[i] == id)
                        {
                            found = true;
                            foundids.Add(id);
                            break;
                        }
                    }

                    if (!found) {
                        Console.WriteLine("Warning: File " + Path.GetFileName(file) + " was not originally in opusinfo, skipping...");
                    }
                } catch (FormatException) {
                    Console.WriteLine("Warning: Filename " + Path.GetFileName(file) + " isn't hash, skipping...");
                }
            }

            Console.WriteLine("Found " + foundids.Count + " files to pack.");

            // Open wavs to read
            Stream[] modStreams = new Stream[foundids.Count];
            for (int i = 0; i < foundids.Count; i++)
            {
                modStreams[i] = new FileStream(Path.Combine(inputDir, foundids[i] + ".wav"), FileMode.Open, FileAccess.Read);
            }

            Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp"));

            List<int> pakstowrite = new List<int>();
            for (int i = 0; i < foundids.Count; i++)
            {
                string wavFilename = foundids[i] + ".wav";
                string opusFilename = foundids[i] + ".opus";
                
                string inpath = Path.Combine(inputDir, wavFilename);
                string tmpOpus = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp\\"+opusFilename);
                string tmpWav = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp\\"+wavFilename);

                var proc = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "opusenc.exe"))
                {
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    Arguments = $" \"{inpath}\" \"{tmpOpus}\" --serial 42 --quiet --padding 0 --vbr --comp 10 --framesize 20 ",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(proc))
                {
                    p.WaitForExit();
                }

                var procnew = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "opusdec.exe"))
                {
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    Arguments = $" \"{tmpOpus}\" \"{tmpWav}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(procnew))
                {
                    p.WaitForExit();
                }

                var procn = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "opusenc.exe"))
                {
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    Arguments = $" \"{tmpWav}\" \"{tmpOpus}\" --serial 42 --quiet --padding 0 --vbr --comp 10 --framesize 20 ",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(procn))
                {
                    p.WaitForExit();
                }

                Console.WriteLine("Processing " + (i+1) + "/" + foundids.Count + ": " + wavFilename);
                for (int e = 0; e < info.OpusCount; e++)
                {
                    if (foundids[i] == info.OpusHashes[e])
                    {
                        int pakIdx = info.PackIndices[e];

                        info.WriteOpusToPak(
                            new MemoryStream(File.ReadAllBytes(tmpOpus)), 
                            ref modStreams[i], 
                            foundids[i], 
                            new MemoryStream(File.ReadAllBytes(tmpWav))
                        );

                        File.Delete(tmpOpus);
                        File.Delete(tmpWav);

                        if (!pakstowrite.Contains(pakIdx))
                        {
                            pakstowrite.Add(pakIdx);
                        }
                    }
                }
            }

            Console.WriteLine("Will write " + pakstowrite.Count + " paks.");

            for (int i = 0; i < pakstowrite.Count; i++)
            {
                var temp = modStreams[i];
                byte[] bytes = new byte[temp.Length];
                temp.Position = 0;
                temp.Read(bytes, 0, Convert.ToInt32(temp.Length));
                string outTemp = Path.Combine(outputDir, "sfx_container_" + pakstowrite[i] + ".opuspak");
                File.WriteAllBytes(outTemp, bytes);
                Console.WriteLine("Wrote " + (i + 1) + "/" + pakstowrite.Count + " paks.");
            }
            
            info.WriteOpusInfo(new DirectoryInfo(outputDir));

            for (int i = 0; i < modStreams.Length; i++)
            {
                modStreams[i].Dispose();
                modStreams[i].Close();
            }
            return 0;
        }
    }
}
