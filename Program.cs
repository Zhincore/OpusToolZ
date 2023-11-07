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
                Console.WriteLine("Not an opusinfo file");
                return 1;
            }

            OpusInfo info = new OpusInfo(fs);

            switch (command) {
                case "info":
                    return Info(args, info);
                case "extract":
                    return Extract(args, info, GetStreams(opusinfo, info));
                case "repack":
                    return Repack(args, info, GetStreams(opusinfo, info));
                default:
                    Console.WriteLine("Unknown subcommand");
                    return 1;
            }

        }

        static Stream[] GetStreams(string opusinfo, OpusInfo info) {
            List<UInt16> tempolisto = new List<UInt16>();
            for (int i = 0; i < info.OpusCount; i++)
            {
                if (!tempolisto.Contains(info.PackIndices[i]))
                {
                    tempolisto.Add(info.PackIndices[i]);
                }
            }

            UInt32 numOfPaks = Convert.ToUInt32(tempolisto.Count);

             // Prepare opuspak streams
            uint[] files = Directory.GetFiles(Path.GetDirectoryName(opusinfo), "*.opuspak").Select(n => GetIndexOfPakName(n)).ToArray();

            Stream[] streams = new Stream[numOfPaks];
            for (int i = 0; i < files.Length; i++)
            {
                streams[files[i]] = new FileStream(
                    Path.Join(Path.GetDirectoryName(opusinfo), "sfx_container_"+files[i]+".opuspak"), 
                    FileMode.Open, 
                    FileAccess.Read
                );
            }
            
            return streams;
        }

        static UInt32 GetIndexOfPakName(string name) {
            return Convert.ToUInt32(name.Replace(".opuspak", string.Empty).Substring(name.LastIndexOf('_') + 1));
        }

        static int Info(string[] args, OpusInfo info) {
            string output = args[2].Replace("\"", string.Empty);
            
            string json = JsonConvert.SerializeObject(info);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, json);

            return 0;
        }

        static int Extract(string[] args, OpusInfo info, Stream[] streams) {
            string dir = args[2].Replace("\"", string.Empty);

            Console.WriteLine("Awaiting hashes to extract");
            List<UInt32> hashes = new List<UInt32>();
            while (true) {
                string line = Console.ReadLine();
                if (String.IsNullOrWhiteSpace(line)) break;

                try {
                    hashes.Add(Convert.ToUInt32(line));
                } catch (FormatException) {
                    Console.WriteLine("'"+line+"' is not a valid hash!");
                    return 1;
                }
            }

            Directory.CreateDirectory(dir);

            info.WriteOpusesFromPaks(streams, new DirectoryInfo(dir), hashes);
            
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
                            var pakIdx = info.PackIndices[i];
                            if (streams[pakIdx] == null) {
                                Console.WriteLine("Opuspak " + pakIdx + " for file " + foundids[i] + " was not found.");
                                continue;
                            }
                            found = true;
                            foundids.Add(id);
                            break;
                        }
                    }

                    if (!found) {
                        Console.WriteLine("Warning: File " + Path.GetFileName(file) + " was not originally in opusinfo, skipping...");
                    }
                } catch (FormatException) {
                    Console.WriteLine("Warning: Filename '" + Path.GetFileName(file) + "' isn't a valid hash, skipping...");
                }
            }

            Console.WriteLine("Found " + foundids.Count + " files to pack.");

            Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp"));

            List<int> pakstowrite = new List<int>();
            for (int i = 0; i < foundids.Count; i++)
            {
                string wavFilename = foundids[i] + ".wav";
                string opusFilename = foundids[i] + ".opus";
                
                string inpath = Path.Combine(inputDir, wavFilename);
                string tmpOpus = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp\\"+opusFilename);
                string tmpWav = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp\\"+wavFilename);

                int id = -1;
                for (int e = 0; e < info.OpusCount; e++)
                {
                    if (foundids[i] == info.OpusHashes[e])
                    {
                        id = e;
                        break;
                    }
                }

                if (id == -1) {
                    Console.WriteLine("File " + foundids[i] + " not found in opusinfo.");
                    continue;
                }

                int pakIdx = info.PackIndices[id];

                if (streams[pakIdx] == null) {
                    Console.WriteLine("Opuspak " + pakIdx + " for file " + foundids[i] + " was not found.");
                    continue;
                }

                var proc = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "opusenc.exe"))
                {
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    Arguments = $" \"{inpath}\" \"{tmpOpus}\" --serial 42 --quiet --padding 0 --vbr --comp 10 --framesize 20 ",
                    UseShellExecute = false,
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
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(procn))
                {
                    p.WaitForExit();
                }

                

                info.WriteOpusToPak(
                    new MemoryStream(File.ReadAllBytes(tmpOpus)), 
                    ref streams[pakIdx], 
                    foundids[i], 
                    new MemoryStream(File.ReadAllBytes(tmpWav))
                );

                File.Delete(tmpOpus);
                File.Delete(tmpWav);

                if (!pakstowrite.Contains(pakIdx))
                {
                    pakstowrite.Add(pakIdx);
                }
                Console.WriteLine("Processed file " + (i+1) + "/" + foundids.Count + ": " + wavFilename);
            }

            Console.WriteLine("Will write " + pakstowrite.Count + " paks.");

            foreach (var paxIdx in pakstowrite)
            {
                var temp = streams[paxIdx];
                byte[] bytes = new byte[temp.Length];
                temp.Position = 0;
                temp.Read(bytes, 0, Convert.ToInt32(temp.Length));
                string outTemp = Path.Combine(outputDir, "sfx_container_" + paxIdx + ".opuspak");
                File.WriteAllBytes(outTemp, bytes);
                Console.WriteLine("Wrote pak " + paxIdx + ".");
            }
            
            info.WriteOpusInfo(new DirectoryInfo(outputDir));

            return 0;
        }
    }
}
