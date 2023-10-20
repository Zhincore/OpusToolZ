using System;
using System.IO;
using System.Linq;
using WolvenKit.Modkit.RED4.Opus;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpusToolZ
{
    class Program
    {
        static int Main(string[] args)
        {
            string opusinfo = args[0].Replace("\"", string.Empty);
            string dir = args[1].Replace("\"", string.Empty);

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

            if (!Directory.Exists(dir))
            {
                Console.WriteLine("Invalid output directory! create it");
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

            Console.WriteLine("Found " + numOfPaks + " paks and " + info.OpusCount + " opuses");

            string[] files = Directory.GetFiles(Path.GetDirectoryName(opusinfo), "*.opuspak").OrderBy(_ => Convert.ToUInt32(_.Replace(".opuspak", string.Empty).Substring(_.LastIndexOf('_') + 1))).ToArray();
            Stream[] streams = new Stream[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                Console.WriteLine("Loading pak " + (i+1) + "/" + files.Length);
                streams[i] = new FileStream(files[i], FileMode.Open, FileAccess.Read);
            }
            
            if(files.Length != numOfPaks)
            {
                Console.WriteLine("Not all of " + Convert.ToString(numOfPaks) + " .opuspak files are present in the directory of sfx_container.opusinfo");
                return 1;
            }

            
            info.WriteAllOpusFromPaks(streams, new DirectoryInfo(dir));
            Console.WriteLine("output: " + dir);

            return 0;            
        }
    }
}
