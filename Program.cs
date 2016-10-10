using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace USB_Filler
{
    internal class Program
    {
        private static readonly List<string> DrivesToCopyTo = new List<string>();
        private static int _repeatCounter = 0;

        private static void Main(string[] args)
        {
            Options options = new Options();
            if (Parser.Default.ParseArguments(args, options))
            {
                if (options.Repeat)
                {
                    Console.WriteLine("Starting in continuous mode.");
                    do
                    {
                        MainLoop(options);

                        _repeatCounter++;

                        Console.WriteLine("\nThis was run {0} with {1} drives each, you should be at {2} drives total.\n", _repeatCounter, options.Drives, _repeatCounter*options.Drives);
                        Console.WriteLine("\nHit Enter for another run, Ctrl+C to exit.\n");
                        Console.ReadKey();
                    } while (options.Repeat);
                }
                else
                {
                    MainLoop(options);
                }
            }
        }

        private static void MainLoop(Options options)
        {

            if (options.Drives != 0)
            {
                Console.WriteLine("Expecting {0} drives to be available.", options.Drives);
                do
                {
                    CheckForDrives(options.CopyToDrives);
                    Console.WriteLine("Found {0} drives to fill.", DrivesToCopyTo.Count);
                    System.Threading.Thread.Sleep(3000);
                } while (options.Drives != DrivesToCopyTo.Count);
            }
            else
            {
                CheckForDrives(options.CopyToDrives);
                Console.WriteLine("Found {0} drives to fill.", DrivesToCopyTo.Count);
            }

            if (options.Format)
            {
                FormatingStuffInParallel(DrivesToCopyTo);
            }

            CopyStuffInParallel(DrivesToCopyTo, options.SourcePath);

            if (!options.NoVerify)
            {
                CalcMd5(DrivesToCopyTo, options.SourcePath);
            }

        }

        private static void CopyStuffInParallel(List<string> drives, string sourcePath)
        {
            Parallel.ForEach(drives, currentDrive =>
            {
                Console.WriteLine("Started copying to {0}", currentDrive);
                DirectoryCopy(sourcePath, currentDrive, true);
                Console.WriteLine("Copying finished on {0}", currentDrive);
            });
        }

        private static void CheckForDrives(string optionsdrives)
        {
            DrivesToCopyTo.Clear();

            foreach (char drive in optionsdrives)
            {
                var drivePath = drive.ToString() + ":\\";
                if (Directory.Exists(drivePath))
                    DrivesToCopyTo.Add(drivePath);
            }
        }

        private static void FormatingStuffInParallel(List<string> drives)
        {
            Parallel.ForEach(drives, currentDrive =>
            {
                Console.WriteLine("Started formating {0}", currentDrive);
                if (FormatDrive(currentDrive))
                {
                    Console.WriteLine("Formating done {0}", currentDrive);
                }
                else
                {
                    Console.WriteLine("Formating failed {0}", currentDrive);
                }
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
            List<string> fileList = GetFiles(path);
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

        public static bool FormatDrive(string driveLetter, string fileSystem = "NTFS", bool quickFormat = true, int clusterSize = 8192, string label = "", bool enableCompression = false)
        {
            driveLetter = driveLetter[0].ToString();

            if (driveLetter.Length != 1 || !char.IsLetter(driveLetter[0]) || driveLetter.Equals("c") || driveLetter.Equals("C"))
                return false;

            if (!IsUserAdministrator())
            {
                Console.WriteLine("Error: Must have administrative privileges to format drive!");
                return false;
            }

            //query and format given drive         
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"select * from Win32_Volume WHERE DriveLetter = '" + driveLetter + ":'");
            foreach (var o in searcher.Get())
            {
                var vi = (ManagementObject) o;
                vi.InvokeMethod("Format", new object[] { fileSystem, quickFormat, clusterSize, label, enableCompression });
            }

            return true;
        }

        // MEH "System Volume Information" crap about Directory.GetFiles
        static List<string> GetFiles(string folder)
        {
            List<string> files = new List<string>();

            foreach (string file in Directory.GetFiles(folder))
            {
                files.Add(file);
            }
            foreach (string subDir in Directory.GetDirectories(folder))
            {
                if (!subDir.Contains("System Volume Information"))
                {
                    try
                    {
                        GetFiles(subDir);
                    }
                    catch
                    {
                        // swallow, log, whatever
                    }
                }
            }
            return files;
        }

        public static bool IsUserAdministrator()
        {
            //bool value to hold our return value
            bool isAdmin;
            try
            {
                //get the currently logged in user
                WindowsIdentity user = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(user);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                isAdmin = false;
            }
            return isAdmin;
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

        [Option('d', "Drives", DefaultValue = 0, HelpText = "Number of expected Drives.")]
        public int Drives { get; set; }

        [Option('f', "format", DefaultValue = false, HelpText = "Format target drive before copying (must have admin privileges).")]
        public bool Format { get; set; }

        [Option('r', "repeat", DefaultValue = false, HelpText = "Repeat continuously.")]
        public bool Repeat { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            return HelpText.AutoBuild(this, current => HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }
}
