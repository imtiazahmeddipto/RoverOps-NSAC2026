# RoverOps
**Build. Code. Crash. Repeat.**

![RoverOps screenshot](docs/screenshot.png)

## The Challenge
RoverOps was made for the **2026 NASA Space Apps Challenge: Space Mission Design Game**. The challenge asks for a game where players design, manage and simulate a space mission, and learn the trade-offs real mission teams face.

## Our Game
In RoverOps, players run a Mars rover mission. They build a rover from parts, plan the mission with visual code blocks, then press RUN and watch it play out on real Martian terrain. They can't drive the rover live: like real rover teams, they plan ahead, learn from failures and try again.

- **Every choice has a cost:** each part changes mass, power use, speed, climbing ability and price.
- **Limited resources:** mass limits, battery and solar power, a Space Points budget, data storage and lander communication range.
- **More than one solution:** different rover designs can complete the same mission.
- **Clear feedback:** failure messages and star ratings show why a mission worked or failed.
- **Real Mars:** terrain from NASA data, and missions based on Sojourner, Spirit and Opportunity.

**14 missions | 19 rover parts | real NASA landing sites | playable in the browser**

## Play / Run
- **Play online:** [link to your WebGL build]
- **Open in Unity:**
  1. Clone this repository.
  2. Open the project with **Unity 6000.0.62f1** (Universal Render Pipeline).
  3. Open `Assets/Scenes/Missions.unity` and press Play.

## NASA and Partner Space Agency Data Used

### MarsForge (Mars Terrain Generation Tool)
We built MarsForge to create RoverOps' terrain. It selects an area on the NASA Mars Trek map, generates 3D terrain and exports it as an FBX model.

- **Terrain:** built from the USGS MOLA–HRSC elevation data, with procedural craters and details.
- **AI Rock Detection:** RockBoxNet finds real rocks in HiRISE images where coverage exists.
- **Rock Prediction:** a machine learning model trained on HiRISE, CTX, THEMIS and elevation data predicts rocks where HiRISE has no coverage.
- **Soft Ground:** estimated from orbital imagery.

**Technology:** C#/.NET, WPF, Helix Toolkit, GDAL, ONNX Runtime, Python  
**Source Code:** https://github.com/imtiazahmeddipto/MarsForge

| Dataset | Mission / Instrument | Resolution | Use |
|---|---|---|---|
| Viking MDIM 2.1 Colour Mosaic | NASA Viking Orbiters | 232 m/pixel | Map for selecting locations |
| MOLA–HRSC Blended DEM v2 | NASA MGS (MOLA) + ESA Mars Express (HRSC) | 200 m/pixel | Elevation for 3D terrain |
| THEMIS Night-time IR Mosaic v14 | NASA Mars Odyssey (THEMIS) | 100 m/pixel | Rock density estimate |
| CTX Global Mosaic | NASA MRO (CTX) | 5 m/pixel | Rock prediction and soft ground |
| HiRISE DTMs and Images | NASA MRO (HiRISE), Univ. of Arizona, USGS | Up to 25 cm/pixel | Real rock detection and model training |

**Data access:** NASA Mars Trek, USGS Astrogeology, Esri OnMars, University of Arizona HiRISE archive.  
**Note:** where HiRISE images are unavailable, rock positions are predicted, not real individual rocks.

## Real Life Event Missions
Players can relive real moments from Mars rover history in Sector 3 and Sector 4 of the game:

- **Sector 3, Mission 1: First Contact (Sojourner 1997):** Pathfinder landed in Ares Vallis on July 4, 1997, carrying Sojourner, the first rover to explore Mars [1]. Its first analysed rock, "Barnacle Bill", surprised scientists with its high silicon and aluminium [2].
- **Sector 3, Mission 2: Send a Message to Earth (Sojourner 1997):** Sojourner relied on the Pathfinder lander to communicate with Earth [3], and studied the large rock "Yogi" [4].
- **Sector 3, Mission 3: The Dust Storm (Spirit):** dust storms in 2007 cut Spirit's solar power so much that it could hardly operate. In 2005, dust devils cleaned its panels and raised its power from about 60% to 93% [5].
- **Sector 3, Mission 4: The Broken Wheel (Spirit):** Spirit's right front wheel stopped turning in 2006. Driving backwards, the dragged wheel uncovered nearly pure silica, a sign of past hot springs or steam vents [6].
- **Sector 4, Mission 1: Martian Blueberries (Opportunity):** Opportunity landed in Eagle Crater in January 2004 [7] and found hematite "blueberries", evidence of an ancient watery environment [9].
- **Sector 4, Mission 2: Escape from Eagle Crater (Opportunity):** after one failed attempt [8], Opportunity climbed out on March 22, 2004 [7], then explored Mars for 14 years [9].

## References
1. NASA Science. Mars Pathfinder. https://science.nasa.gov/mission/mars-pathfinder/
2. Max Planck Institute for Chemistry, via NASA NSSDCA (1997). First results from the APXS-analyses on Pathfinder. https://nssdc.gsfc.nasa.gov/planetary/text/marspath_pr_971204.txt
3. NASA (1996). Mars Pathfinder Landing Press Kit. https://mars.nasa.gov/internal_resources/816/
4. NASA/JPL (1997). Sojourner, "Barnacle Bill," & "Yogi." PIA00660. https://science.nasa.gov/photojournal/sojourner-barnacle-bill-and-yogi/
5. NASA Science. Spirit. https://science.nasa.gov/mission/mer-spirit/
6. NASA/JPL (2011). NASA's Spirit Rover Completes Mission on Mars. https://www.jpl.nasa.gov/news/nasas-spirit-rover-completes-mission-on-mars
7. NASA Science. Opportunity. https://science.nasa.gov/mission/mer-opportunity/
8. NASA/JPL (2004). A Well-Traveled "Eagle Crater." PIA05633. https://science.nasa.gov/photojournal/a-well-traveled-eagle-crater/
9. NASA Science. Mars Exploration Rovers: Science Highlights. https://science.nasa.gov/mission/mars-exploration-rovers-spirit-and-opportunity/science-highlights/

### 3D Model References
- Rover Model (Reference): https://science.nasa.gov/resource/mars-perseverance-rover-3d-model/
- Lander (Reference): https://science.nasa.gov/3d-resources/apollo-lunar-module/

## Disclaimer
RoverOps is a student project made for the NASA Space Apps Challenge 2026. It uses publicly available NASA and partner data, but it is not made, endorsed or supported by NASA.

## License
[Choose a license, for example MIT]
