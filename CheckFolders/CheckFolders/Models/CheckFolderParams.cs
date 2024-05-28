using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace CheckFolders.Models
{
    public class CheckFolderParams
    {
        [Display(Name= "Folder name")]
        public string FolderName { get; set; } = string.Empty;
        public bool DeleteTempFiles { get; set; } = false;
        public string ResultString { get; set; } = string.Empty;
    }
}
