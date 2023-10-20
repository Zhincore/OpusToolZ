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
            string opusinfo = args[0];
            string filehash = args[1];

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
                if(!tempolisto.Contains(info.PackIndices[i]))
                {
                    tempolisto.Add(info.PackIndices[i]);
                }
            }

            UInt32 numOfPaks = Convert.ToUInt32(tempolisto.Count);

            string[] files = Directory.GetFiles(Path.GetDirectoryName(opusinfo), "*.opuspak").OrderBy(_ => Convert.ToUInt32(_.Replace(".opuspak", string.Empty).Substring(_.LastIndexOf('_') + 1))).ToArray();
            Stream[] streams = new Stream[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                streams[i] = new FileStream(files[i], FileMode.Open, FileAccess.Read);
            }
            
            if(files.Length != numOfPaks)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("All " + Convert.ToString(numOfPaks) + " .opuspak files are not present in the directory of sfx_container.opusinfo");
                return 1;
            }


            UInt32 hash = 0;
            try
            {
                hash = Convert.ToUInt32(filehash);
                if (!info.OpusHashes.Contains(hash))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Opus file hash not present in opusinfo , try again");
                    Console.ResetColor();
                }
            }
            catch { 
                Console.WriteLine("Invalid Hash"); 
                return 1; 
            }


            string output = AppDomain.CurrentDomain.BaseDirectory + filehash + ".opus";
            info.WriteOpusFromPaks(streams, new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory), hash);
            

            Console.WriteLine("output: " + output);
            return 0;            
        }
    }
}
