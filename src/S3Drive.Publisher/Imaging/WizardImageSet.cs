namespace S3Drive.Publisher.Imaging
{
    using System.Collections.Generic;

    /// <summary>
    /// The generated Inno Setup wizard bitmaps, smallest first, so the installer can pick the one
    /// that matches the display's scaling.
    /// </summary>
    public class WizardImageSet
    {
        /// <summary>
        /// The tall welcome and finish page images (164x314 at 100% scaling, then larger).
        /// </summary>
        public List<string> LargeImages { get; set; } = new List<string>();

        /// <summary>
        /// The square inner-page header images (55x55 at 100% scaling, then larger).
        /// </summary>
        public List<string> SmallImages { get; set; } = new List<string>();
    }
}
