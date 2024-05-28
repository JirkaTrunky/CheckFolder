using CheckFolders.Models;
using System.Security.Cryptography;
using System.Text.Json;

namespace CheckFolders.Lib
{
    public enum FileChangeType
    {
        Added,
        Modified,
        Deleted
    }

    public class FileResult
    {
        public string Filename { get; set; } = string.Empty;
        public FileChangeType Type { get; set; }
        public int Version { get; set; }
    }

    public enum FolderChangeType
    {
        NewFolder,
        FolderExisted,
        ErrorOnFolder
    }

    public class FolderResult
    {
        public List<FileResult> Files { get; set; } = new List<FileResult>();

        public FolderChangeType Type { get; set; }

        public string ErrorMsg = string.Empty;
        public FolderResult() { }

    }

    public class CFFileInfo     // CheckFolder File Info  -  nazev FileInfo kolidoval s System.IO
    {
        public string Filename { get; set; } = string.Empty;
        public int Version { get; set; } = 1;
        public string Hash { get; set; } = string.Empty;
        public CFFileInfo() { }

    }

    public class FolderInfo
    {
        public List<CFFileInfo> Files { get; set; } = new List<CFFileInfo> { };
        public FolderInfo() { }
    }

    public class CheckFolders
    {
        CheckFolderParams Params { get; set; }

        public CheckFolders(CheckFolderParams parametry)
        {
            Params = parametry;

            Params.FolderName = Params.FolderName
              .Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
              .Replace("//", "/");
        }

        /// <summary>
        /// Zavolá funkci podle parametrů a výsledek vrátí v parametry.ResultString.
        /// </summary>
        /// <returns>výslený string</returns>
        public string DoWork()
        {
            string s = string.Empty;

            Directory.Exists(Params.FolderName);

            if (Params != null)
            {
                if (Params.DeleteTempFiles)
                    s = DoDeleteTempFiles(Params.FolderName);
                else
                    s = DoCheck(Params.FolderName);

                Params.ResultString = s;
            }

            return s;
        }

        /// <summary>
        /// Najde změny v daném adresáři včetně podadresářů.
        /// Volá se rekurzivně.
        /// </summary>
        /// <param name="folder">adresář, který se zpracuje.</param>
        /// <returns></returns>
        string DoCheck(string folder)
        {
            string s = "Checking: " + folder + "\n";

            CheckSingleFolder(folder);

            try
            { 
                // Recurse into subdirectories of this directory.
                string[] subdirectoryEntries = Directory.GetDirectories(folder);
                foreach (string subdirectory in subdirectoryEntries)
                    s = s + DoCheck(subdirectory);
            }
            catch (System.UnauthorizedAccessException)
            {
            }

            return s;

        }

        /// <summary>
        /// vrátí úplné jméno souboru, ve kterém je uloženo FileInfo (v JSON)
        /// </summary>
        /// <param name="folder"></param>
        /// <returns></returns>
        string GetDbFileName(string folder)
        {
            return Path.Combine(folder, "fileinfo.~db");
        }

        /// <summary>
        /// Zpracuje jeden adresář. Pokud v něm neexistuje databázový soubor, pak ho vytvoří.
        /// Pokud soubor existuje, pak najde rozdíly a vrátí seznam rozdílů
        /// </summary>
        /// <param name="folder"></param>
        FolderResult CheckSingleFolder(string folder)
        {
            try
            {

                if (!File.Exists(folder))
                {
                    var fi = CreateFileInfo(folder);

                    var json = JsonSerializer.Serialize(fi);

                    File.WriteAllText(GetDbFileName(folder), json);




                    return new FolderResult() { Type = FolderChangeType.NewFolder };
                }

                var files = CheckFolderChanges(folder);
                return new FolderResult() { Type = FolderChangeType.FolderExisted, Files = files };
            }
            catch (Exception ex)
            {
                return new FolderResult() { Type = FolderChangeType.ErrorOnFolder, ErrorMsg = ex.Message };
            }

        }

        List<CFFileInfo> CreateFileInfo(string folder)
        {
            List<CFFileInfo> files = new List<CFFileInfo>();

            DirectoryInfo d = new DirectoryInfo(folder);

            FileInfo[] Files = d.GetFiles("*.*");

            using (var md5 = MD5.Create())
            {
                foreach (FileInfo file in Files)
                {
                    var info = new CFFileInfo() { Version = 1, Hash = GetFileHash(file.FullName, md5), Filename = file.Name };

                    files.Add(info);    
                }
            }

            return files;
        }

        string GetFileHash(string filename, HashAlgorithm algorithm)
        {
            using (var stream = File.OpenRead(filename))
            {
                var hash = algorithm.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        List<FileResult> CheckFolderChanges(string folder)
        {
            List<FileResult> files = new List<FileResult>();


            return files;
        }

        string DoDeleteTempFiles(string folder)
        {
            var fn = GetDbFileName(folder);

            string s = "Deleting: " + fn + "\n";

            if (File.Exists(s))
            {
                File.Delete(fn);
            }

            try
            {
                // projdi podadresare
                string[] subdirectoryEntries = Directory.GetDirectories(folder);
                foreach (string subdirectory in subdirectoryEntries)
                    s = s + DoDeleteTempFiles(subdirectory);
            }
            catch (System.UnauthorizedAccessException)
            {
                s += "Unauthorize dAccess Exception on folder: " + folder;
            }

            return s;
        }
    }
}
