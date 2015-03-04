﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace USB_Filler
{
    internal class Program
    {
        private static readonly List<string> DrivesToCopyTo = new List<string>();

        private static void Main(string[] args)
        {
            Options options = new Options();
            if (Parser.Default.ParseArguments(args, options))
            {
                foreach (var drive in options.CopyToDrives)
                {
                    var drivePath = drive.ToString() + ":\\";
                    if (Directory.Exists(drivePath))
                        DrivesToCopyTo.Add(drivePath);
                }

                Console.WriteLine("\nFound {0} drives to fill.\n", DrivesToCopyTo.Count);

                CopyStuffInParallel(DrivesToCopyTo, options.SourcePath);

                if (!options.NoVerify)
                {
                    CalcMd5(DrivesToCopyTo, options.SourcePath);
                }
            }
        }

        private static void CopyStuffInParallel(List<string> drives, string sourcePath)
        {
            Parallel.ForEach(drives, currentDrive =>
            {
                Console.WriteLine("Copying {0}", currentDrive);
                DirectoryCopy(sourcePath, currentDrive, true);
                Console.WriteLine("Finished {0}", currentDrive);
            });
        }


        //
        // Shamelessly taken from https://msdn.microsoft.com/de-de/library/bb762914%28v=vs.110%29.aspx
        //
        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);
            DirectoryInfo[] dirs = dir.GetDirectories();

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);
            }

            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, true);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, true);
                }
            }
        }

        private static void CalcMd5(List<string> drives, string sourcePath)
        {
            var sourceMd5 = PathMd5(sourcePath);
            var sourceFiles = new List<string>(sourceMd5.Keys);

            Parallel.ForEach(drives, drive =>
            {
                Console.WriteLine("MD5 Check: " + drive);
                Dictionary<string, string> targetMd5 = PathMd5(drive);
                var targetFiles = new List<string>(targetMd5.Keys);

                bool forwardCheck = true;
                bool md5Check = true;

                sourceFiles.ForEach(s =>
                {
                    if (!targetFiles.Contains(s))
                    {
                        Console.WriteLine(s);
                        forwardCheck = false;
                    }
                });

                foreach (var file in sourceFiles)
                {
                    if (sourceMd5[file] != targetMd5[file])
                    {
                        md5Check = false;
                    }
                }


                if (forwardCheck && md5Check)
                {
                    Console.WriteLine("MD5 is good on: " + drive);
                }
                else
                {
                    Console.WriteLine("Something is fucked up on: " + drive);
                    Console.ReadKey();
                }
            });
        }

        private static Dictionary<string, string> PathMd5(string path)
        {
            List<string> fileList = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList();
            Dictionary<string, string> fileXmd5 = new Dictionary<string, string>();

            Parallel.ForEach(fileList, file =>
            {
                var barePath = file.Replace(path, "");
                barePath = barePath.TrimStart('\\');

                fileXmd5.Add(barePath, FileMd5(file));
            });

            return fileXmd5;
        }

        private static string FileMd5(string filePath)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] b = md5.ComputeHash(stream);
                    stream.Close();
                    return BitConverter.ToString(b).Replace("-", "").ToLower();
                }
            }
        }
    }

    internal class Options
    {
        [Option('s', "source", Required = true, HelpText = "Input path to be processed.")]
        public string SourcePath { get; set; }

        [Option('c', "copyto", Required = true, HelpText = "Drives to copy to (e.g. ABCD).")]
        public string CopyToDrives { get; set; }

        [Option('n', "no-verify", DefaultValue = false, HelpText = "Skips verification of the target drives.")]
        public bool NoVerify { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            return HelpText.AutoBuild(this, (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }
}
