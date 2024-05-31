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

        public override string ToString()
        {
            string s= string.Empty;

            switch (Type)
            {
                case FileChangeType.Added: s = "[A] " + Filename; break;
                case FileChangeType.Modified: s = "[M] " + Filename + " (ve verzi " + Version + ") "; break;
                case FileChangeType.Deleted: s = "[D] " + Filename; break;
            }

            return s ;
        }
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

        public override string ToString()
        {
            string s= string.Empty;
            switch (Type)
            {
                case FolderChangeType.NewFolder: 
                    s = "Nový adresář.";
                    break;

                case FolderChangeType.FolderExisted:
                    if(Files.Count == 0)
                    {
                        s += "Žádné změny.";
                        break;
                    }

                    foreach (var file in Files)
                    {
                        s += file.ToString() + "\n";
                    }
                    break;

                case FolderChangeType.ErrorOnFolder:
                        s += ErrorMsg;
                    break;
            }

            return s;
        }
    }

    /// <summary>
    /// // CheckFolder File Info  -  nazev FileInfo kolidoval s System.IO
    /// </summary>
    public class CFFileInfo  
    {
        public string Hash { get; set; } = string.Empty;
        public int Version { get; set; } = 1;

        public bool StillExists = false;        // poznamenavam si, ze soubor stale existuje, abych vedel, ktere jsou Deleted

    }

    public class FileInfoDictionary : Dictionary<string, CFFileInfo>;
    public class FolderInfo
    {
        public FileInfoDictionary Files { get; set; } = new();
        public FolderInfo() { }
    }

    public class CheckFolders
    {
        // nazev souboru, ve kterem je ulozena informace FileInfo
        const string DbFilename = "fileinfo.~db";    

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
        /// <returns> v Params.ResultString >výslený string</returns>
        public void DoWork()
        {
            if (Params == null) throw new ArgumentNullException(nameof(Params));    // TODO nameof dat vsude
                
            string s = string.Empty;

            if (!Directory.Exists(Params.FolderName))
            {
                s = "Složka nebyla nalezena.";
            }
            else
            {
                if (!Params.DeleteTempFiles)
                    s = DoCheck(Params.FolderName);
                else
                    s = DoDeleteTempFiles(Params.FolderName);
            }

            Params.ResultString = s;
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

            var fr = CheckSingleFolder(folder);
            s += fr.ToString();

            try
            { 
                // Recurse into subdirectories of this directory.
                string[] subdirectoryEntries = Directory.GetDirectories(folder);
                foreach (string subdirectory in subdirectoryEntries)
                    s = s + DoCheck(subdirectory);
            }
            catch (System.UnauthorizedAccessException)
            {
                s += "Unauthorized Access Exception on folder: " + folder;
            }

            return s;

        }

        /// <summary>
        /// vrátí úplné jméno souboru, ve kterém je uloženo FileInfo (v JSON)
        /// </summary>
        /// <param name="folder"></param>
        /// <returns></returns>
        string GetDbFilename(string folder)
        {
            return Path.Combine(folder, DbFilename);
        }

        /// <summary>
        /// Zpracuje jeden adresář. Pokud v něm neexistuje databázový soubor, pak ho vytvoří.
        /// Pokud soubor existuje, pak najde rozdíly a vrátí seznam rozdílů
        /// </summary>
        /// <param name="folder"></param>
        FolderResult CheckSingleFolder(string folder)
        {
            var dbFilename = GetDbFilename(folder);

            try
            {
                if (!File.Exists(dbFilename))
                {
                    var fi = CreateFileInfo(folder);

                    SaveFileInfo(dbFilename, fi);

                    return new FolderResult() { Type = FolderChangeType.NewFolder };
                }
                else
                {
                    var oldJson = File.ReadAllText(dbFilename);
                    FileInfoDictionary? oldInfo = JsonSerializer.Deserialize<FileInfoDictionary>(oldJson);

                    if (oldInfo == null) throw new Exception("Chyba pri nacitani informaci z " + dbFilename);

                    var newInfo = CreateFileInfo(folder);

                    // provede porovnani adresaru, meni take newInfo
                    var files = CheckFolderChanges(folder, oldInfo, newInfo);

                    SaveFileInfo(dbFilename, newInfo);

                    return new FolderResult() { Type = FolderChangeType.FolderExisted, Files = files };
                }
            }
            catch (Exception ex)
            {
                return new FolderResult() { Type = FolderChangeType.ErrorOnFolder, ErrorMsg = ex.Message };
            }

        }

        void SaveFileInfo(string dbFilename, FileInfoDictionary fi)
        {
            var json = JsonSerializer.Serialize(fi);

            File.SetAttributes(dbFilename, 0);

            File.WriteAllText(dbFilename, json);

            File.SetAttributes(dbFilename,
                FileAttributes.Hidden |
                FileAttributes.ReadOnly);
        }
        
        FileInfoDictionary CreateFileInfo(string folder)
        {
            FileInfoDictionary files = new();

            DirectoryInfo d = new DirectoryInfo(folder);

            FileInfo[] Files = d.GetFiles("*.*");

            using (var md5 = MD5.Create())
            {
                foreach (FileInfo file in Files)
                {
                    // preskocim soubor s FileInfo
                    if(file.Name.Equals(DbFilename)) continue;

                    var info = new CFFileInfo() { Version = 1, Hash = GetFileHash(file.FullName, md5)};

                    files.Add(file.Name, info);    
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

        List<FileResult> CheckFolderChanges(string folder, FileInfoDictionary oldFiles, FileInfoDictionary newFiles)
        {
            if (oldFiles == null) throw new ArgumentNullException("oldFiles");

            List <FileResult> results = new List<FileResult>();

            foreach (var newInfo in newFiles)
            {
                FileResult ? result;

                if(oldFiles.ContainsKey(newInfo.Key))
                { 
                    // soubor jiz existoval, porovnám Hash
                    CFFileInfo oldInfo = oldFiles[newInfo.Key];

                    oldInfo.StillExists = true;

                    if (oldInfo.Hash.Equals(newInfo.Value.Hash))
                    {
                        // stejný soubor
                        result = null;      // nezmenene soubory nevypisuji
                    }
                    else
                    {
                        // soubor byl zmenen, nova verze
                        result = new FileResult() { Filename = newInfo.Key, Type = FileChangeType.Modified, Version = newInfo.Value.Version + 1 };
                    }
                }
                else
                {
                    // novy soubor
                    result = new FileResult() { Filename = newInfo.Key, Type = FileChangeType.Added, Version = 1 };
                }

                if(result != null) results.Add(result);
            }

            return results;
        }

        string DoDeleteTempFiles(string folder)
        {
            var fn = GetDbFilename(folder);

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
                s += "Unauthorized Access Exception on folder: " + folder;
            }

            return s;
        }
    }
}
