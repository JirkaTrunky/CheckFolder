using CheckFolders.Models;
using System.Security.Cryptography;
using System.Text.Json;

namespace CheckFolders.Lib
{
    /// <summary>
    /// Typ zmeny na souboru. Soubory, ktere nebyly zmeneny si nepamatuji. 
    /// </summary>
    public enum FileChangeType
    {
        Added,
        Modified,
        Deleted
    }

    /// <summary>
    /// vysledem analyzy jednoho souboru
    /// </summary>
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

    /// <summary>
    /// Typ zmen na adresari nebo chyba.
    /// </summary>
    public enum FolderChangeType
    {
        NewFolder,
        FolderExisted,
        ErrorOnFolder
    }

    /// <summary>
    /// Vysledek analyzy adresare
    /// </summary>
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
    /// CheckFolder File Info  -  nazev FileInfo kolidoval s System.IO
    /// Informace o jednom souboru, ktere se ukladaji do db souboru
    /// </summary>
    public class CFFileInfo  
    {
        public string Hash { get; set; } = string.Empty;
        public int Version { get; set; } = 1;

        public bool StillExists = false;        // poznamenavam si, ze soubor stale existuje, abych vedel, ktere jsou Deleted

    }

    /// <summary>
    /// slovnik ifnormace o souboru. Klicem je nazev souboru.
    /// </summary>
    public class FolderInfo : Dictionary<string, CFFileInfo>;

    /// <summary>
    /// trida, ktera zajistuje samotne poravnani adresare a podadresaru nebo vymaze db soubor
    /// </summary>
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
                    FolderInfo? oldInfo = JsonSerializer.Deserialize<FolderInfo>(oldJson);

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

        /// <summary>
        /// Ulozi FileInfo do db souboru. Nastavi atributy souboru na Hidden a ReadOnly
        /// </summary>
        /// <param name="dbFilename"></param>
        /// <param name="fi"></param>
        void SaveFileInfo(string dbFilename, FolderInfo fi)
        {
            var json = JsonSerializer.Serialize(fi);

            File.SetAttributes(dbFilename, 0);

            File.WriteAllText(dbFilename, json);

            File.SetAttributes(dbFilename,
                FileAttributes.Hidden |
                FileAttributes.ReadOnly);
        }
        
        /// <summary>
        ///  Projde vsechny soubory v jednom adresari a vrati informace o nich (FileInfo)
        /// </summary>
        /// <param name="folder"></param>
        /// <returns></returns>
        FolderInfo CreateFileInfo(string folder)
        {
            FolderInfo files = new();

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

        /// <summary>
        /// Spocita hash jednoho souboru danym algoritmem
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="algorithm"></param>
        /// <returns></returns>
        string GetFileHash(string filename, HashAlgorithm algorithm)
        {
            using (var stream = File.OpenRead(filename))
            {
                var hash = algorithm.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        ///  porovna stary adresar s novym a najde zmeny. 
        /// </summary>
        /// <param name="folder">adresar, ktery se zpracovava, neprochazi se podadresare</param>
        /// <param name="oldFiles">informace o starem adresari</param>
        /// <param name="newFiles">informace o novem adresari - zde se vraci opravene cislo verze. Toto je treba ulozit do db souboru</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        List<FileResult> CheckFolderChanges(string folder, FolderInfo oldFiles, FolderInfo newFiles)
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
                        newInfo.Value.Version++;        // inkremuntuje verzi v newInfo

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

        /// <summary>
        /// vymaze db soubory s FileInfo z adresare vcetne podadresaru.
        /// </summary>
        /// <param name="folder"></param>
        /// <returns></returns>
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
