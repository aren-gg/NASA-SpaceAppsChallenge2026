namespace MarsSurvival.UI
{
    /// <summary>
    /// Every dataset, reference, library and font the game uses, with who made it, where it is
    /// published and what the game uses it for. The title screen's Credits card is built from
    /// this list, and CREDITS.md at the repository root mirrors it; keep the two in step.
    ///
    /// Format follows the data publishers' own guidance: PDS asks for the data set title, the
    /// producing team and "NASA Planetary Data System" as publisher (with a DOI or ID where one
    /// exists); CCMC asks for its acknowledgement sentence for DONKI; fonts under the SIL Open
    /// Font License are credited with author and licence.
    /// </summary>
    public static class GameCredits
    {
        public sealed class Entry
        {
            public readonly string Title, Credit, UsedFor, Url;
            public Entry(string title, string credit, string usedFor, string url)
            {
                Title = title; Credit = credit; UsedFor = usedFor; Url = url;
            }
        }

        public sealed class Section
        {
            public readonly string Name;
            public readonly Entry[] Entries;
            public Section(string name, params Entry[] entries) { Name = name; Entries = entries; }
        }

        public const string MadeFor = "Made for the NASA Space Apps Challenge 2026.";

        public const string Disclaimer =
            "Marsivity uses publicly released NASA, USGS and partner data. NASA, USGS and the instrument teams do not " +
            "sponsor or endorse this game. Data are used as provided, simplified for play, and are not forecasts.";

        /// <summary>CCMC's requested acknowledgement for DONKI (CCMC publications policy).</summary>
        public const string CcmcAcknowledgement =
            "We acknowledge the Community Coordinated Modeling Center (CCMC) at Goddard Space Flight Center for the use of DONKI, " +
            "https://ccmc.gsfc.nasa.gov/tools/DONKI/.";

        public static readonly Section[] Sections =
        {
            new Section("Mars data",
                new Entry("Curiosity Radiation Assessment Detector (RAD)",
                    "MSL RAD Reduced Data Records, MSL-M-RAD-3-RDR-V1.0. MSL RAD team, Southwest Research Institute. " +
                    "NASA Planetary Data System, Planetary Plasma Interactions Node.",
                    "Daily galactic cosmic ray dose at Gale Crater, the background radiation for every sol.",
                    "https://pds-ppi.igpp.ucla.edu/data/MSL-M-RAD-3-RDR-V1.0/AAREADME.TXT"),
                new Entry("Curiosity Rover Environmental Monitoring Station (REMS)",
                    "MSL REMS data. REMS team, Centro de Astrobiología (Spain). NASA Planetary Data System, Atmospheres Node.",
                    "The shape and range of each sol's temperature swing.",
                    "https://atmos.nmsu.edu/data_and_services/atmospheres_data/MARS/curiosity/rems.html"),
                new Entry("DONKI, Space Weather Database of Notifications, Knowledge, Information",
                    "Community Coordinated Modeling Center (CCMC), NASA Goddard Space Flight Center. " + CcmcAcknowledgement,
                    "Recorded solar flare levels replayed sol by sol to drive solar particle events.",
                    "https://ccmc.gsfc.nasa.gov/tools/DONKI/"),
                new Entry("HRSC and MOLA Blended Digital Elevation Model, 200 m (v2)",
                    "Mars_HRSC_MOLA_BlendDEM_Global_200mp_v2. Fergason, Hare and Laura, USGS Astrogeology Science Center (2018). " +
                    "Blends MGS MOLA altimetry with Mars Express HRSC stereo (ESA, DLR, FU Berlin).",
                    "The Gale Crater terrain you walk and drive on (1,025 x 1,025 samples at 200 m).",
                    "https://planetarymaps.usgs.gov/mosaic/Mars/HRSC_MOLA_Blend/"),
                new Entry("Mars Global Surveyor MOLA Gridded Data Records (MEGDR)",
                    "MGS MOLA Mission Experiment Gridded Data Records. MOLA Science Team. " +
                    "NASA Planetary Data System, Geosciences Node, Washington University in St. Louis.",
                    "The MOLA elevation datum the terrain is measured against.",
                    "https://pds-geosciences.wustl.edu/missions/mgs/megdr.html"),
                new Entry("Mars MGS MOLA DEM 463 m",
                    "Mars Global Surveyor MOLA digital elevation model. USGS Astrogeology Science Center.",
                    "Cross-check of landing-site elevation on the MOLA datum.",
                    "https://astrogeology.usgs.gov/search/map/mars_mgs_mola_dem_463m"),
                new Entry("Mars24 Sunclock algorithm",
                    "NASA Goddard Institute for Space Studies (Allison and McEwen, 2000).",
                    "Sun position, local solar time and Sun distance for day, night and solar power.",
                    "https://www.giss.nasa.gov/tools/mars24/help/algorithm.html")),

            new Section("Standards and research",
                new Entry("NASA-STD-3001, Volume 1, Revision C: Crew Health",
                    "NASA Space Flight Human-System Standard. NASA Office of the Chief Health and Medical Officer.",
                    "The career radiation limit behind the dose budget, scaled for play.",
                    "https://standards.nasa.gov/sites/default/files/standards/NASA/C//nasa-std-3001-vol-1-rev-c-signature.pdf"),
                new Entry("Veggie: Space Vegetables for the International Space Station and Beyond",
                    "Massa, G. D. (2016). NASA Kennedy Space Center. NASA Technical Reports Server, 20160004060.",
                    "Per-plant water estimates for the leafy greenhouse bed.",
                    "https://ntrs.nasa.gov/citations/20160004060"),
                new Entry("Veggie Plant Growth System",
                    "NASA, International Space Station program.",
                    "Which crops grow in the greenhouse and how the bed is laid out.",
                    "https://www.nasa.gov/missions/station/veggie-plant-growth-system-activated-on-international-space-station"),
                new Entry("Mars Oxygen ISRU Experiment (MOXIE)",
                    "Hecht, M. et al. (2021). Space Science Reviews 217, 9. doi:10.1007/s11214-020-00782-8.",
                    "Background on making oxygen on Mars. The game's oxygen plant is a simpler water-electrolysis model.",
                    "https://doi.org/10.1007/s11214-020-00782-8")),

            new Section("Software and fonts",
                new Entry("Unity 2022.3, Universal Render Pipeline, TextMesh Pro, uGUI",
                    "Unity Technologies. Unity Companion License and Unity terms.",
                    "Engine, rendering and interface.",
                    "https://unity.com/legal/licenses/unity-companion-license"),
                new Entry("Barlow Condensed",
                    "The Barlow Project Authors (Jeremy Tribby). SIL Open Font License 1.1.",
                    "All interface text and readouts.",
                    "https://github.com/jpt/barlow"),
                new Entry("VT323",
                    "The VT323 Project Authors (Peter Hull). SIL Open Font License 1.1.",
                    "Fallback for symbols the main font lacks.",
                    "https://github.com/phoikoi/VT323"),
                new Entry("Liberation Sans",
                    "Red Hat, via TextMesh Pro. SIL Open Font License 1.1.",
                    "Last-resort fallback font.",
                    "https://github.com/liberationfonts/liberation-fonts")),
        };
    }
}
