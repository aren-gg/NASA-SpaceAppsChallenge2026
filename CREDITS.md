# Credits

Marsivity credits every dataset, reference, library and font it uses. This file mirrors the in-game Credits screen (`My project/Assets/Scripts/UI/GameCredits.cs`); keep the two in step.

Made for the NASA Space Apps Challenge 2026.

## Mars data

- **Curiosity Radiation Assessment Detector (RAD)**  
  MSL RAD Reduced Data Records, MSL-M-RAD-3-RDR-V1.0. MSL RAD team, Southwest Research Institute. NASA Planetary Data System, Planetary Plasma Interactions Node.  
  Used for: Daily galactic cosmic ray dose at Gale Crater, the background radiation for every sol.  
  Source: <https://pds-ppi.igpp.ucla.edu/data/MSL-M-RAD-3-RDR-V1.0/AAREADME.TXT>

- **Curiosity Rover Environmental Monitoring Station (REMS)**  
  MSL REMS data. REMS team, Centro de Astrobiología (Spain). NASA Planetary Data System, Atmospheres Node.  
  Used for: The shape and range of each sol's temperature swing.  
  Source: <https://atmos.nmsu.edu/data_and_services/atmospheres_data/MARS/curiosity/rems.html>

- **DONKI, Space Weather Database of Notifications, Knowledge, Information**  
  Community Coordinated Modeling Center (CCMC), NASA Goddard Space Flight Center. We acknowledge the Community Coordinated Modeling Center (CCMC) at Goddard Space Flight Center for the use of DONKI, https://ccmc.gsfc.nasa.gov/tools/DONKI/.  
  Used for: Recorded solar flare levels replayed sol by sol to drive solar particle events.  
  Source: <https://ccmc.gsfc.nasa.gov/tools/DONKI/>

- **MSL Gale Merged DEM 1 m (v3), from HiRISE stereo**  
  MSL_Gale_DEM_Mosaic_1m_v3. USGS Astrogeology Science Center mosaic of MRO HiRISE stereo DTMs. NASA/JPL/University of Arizona, USGS.  
  Used for: The 4 km patch of 1 m ground around the outpost at Bradbury Landing.  
  Source: <https://astrogeology.usgs.gov/search/map/mars_msl_gale_merged_dem_1m>

- **HRSC and MOLA Blended Digital Elevation Model, 200 m (v2)**  
  Mars_HRSC_MOLA_BlendDEM_Global_200mp_v2. Fergason, Hare and Laura, USGS Astrogeology Science Center (2018). Blends MGS MOLA altimetry with Mars Express HRSC stereo (ESA, DLR, FU Berlin).  
  Used for: The Gale Crater terrain you walk on (1,025 x 1,025 samples at 200 m).  
  Source: <https://planetarymaps.usgs.gov/mosaic/Mars/HRSC_MOLA_Blend/>

- **Mars Global Surveyor MOLA Gridded Data Records (MEGDR)**  
  MGS MOLA Mission Experiment Gridded Data Records. MOLA Science Team. NASA Planetary Data System, Geosciences Node, Washington University in St. Louis.  
  Used for: The MOLA elevation datum the terrain is measured against.  
  Source: <https://pds-geosciences.wustl.edu/missions/mgs/megdr.html>

- **Mars MGS MOLA DEM 463 m**  
  Mars Global Surveyor MOLA digital elevation model. USGS Astrogeology Science Center.  
  Used for: Cross-check of landing-site elevation on the MOLA datum.  
  Source: <https://astrogeology.usgs.gov/search/map/mars_mgs_mola_dem_463m>

- **Mars24 Sunclock algorithm**  
  NASA Goddard Institute for Space Studies (Allison and McEwen, 2000).  
  Used for: Sun position, local solar time and Sun distance for day and night.  
  Source: <https://www.giss.nasa.gov/tools/mars24/help/algorithm.html>

## Characters and animation

- **Astronaut (NASA 3D Resources)**  
  NASA 3D Resources; model by DigitalSpace Corporation.  
  Used for: The astronaut's EVA suit and sealed helmet, detailed in Blender after "Astronaut by Jungle Jim" (Sketchfab, CC BY 4.0, <https://sketchfab.com/3d-models/astronaut-by-jungle-jim-c775a977606a4e4e990afe783c6bc845>) as design reference.  
  Source: <https://science.nasa.gov/3d-resources/astronaut/>

- **CMU Graphics Lab Motion Capture Database**  
  Carnegie Mellon University (<http://mocap.cs.cmu.edu/>), created with funding from NSF EIA-0196217; BVH conversion by Bruce Hahne (<https://github.com/meshula/cmu-mocap>). Free for research and commercial use.  
  Used for: The astronaut's idle, walk, run, sprint and jump motion (subjects 16 and 77), retargeted onto the suit in Blender.  
  Source: <http://mocap.cs.cmu.edu/>

## Software and fonts

- **Unity 2022.3, Universal Render Pipeline, TextMesh Pro, uGUI**  
  Unity Technologies. Unity Companion License and Unity terms.  
  Used for: Engine, rendering and interface.  
  Source: <https://unity.com/legal/licenses/unity-companion-license>

- **Barlow Condensed**  
  The Barlow Project Authors (Jeremy Tribby). SIL Open Font License 1.1.  
  Used for: All interface text and readouts.  
  Source: <https://github.com/jpt/barlow>

- **VT323**  
  The VT323 Project Authors (Peter Hull). SIL Open Font License 1.1.  
  Used for: Fallback for symbols the main font lacks.  
  Source: <https://github.com/phoikoi/VT323>

- **Liberation Sans**  
  Red Hat, via TextMesh Pro. SIL Open Font License 1.1.  
  Used for: Last-resort fallback font.  
  Source: <https://github.com/liberationfonts/liberation-fonts>

## Disclaimer

Marsivity uses publicly released NASA, USGS and partner data. NASA, USGS and the instrument teams do not sponsor or endorse this game. Data are used as provided, simplified for play, and are not forecasts.
