using System;

namespace MarsSurvival.Simulation
{
    public static class ResearchCatalog
    {
        public static readonly ResearchNode[] Nodes =
        {
            Node("H0", "Controlled grow beds", "Health", "Crops use 20% less water and nutrients and tolerate stress better.", 12, 4),
            Node("H1", "Nutrition program", "Health", "Harvests give 25% more food; meals restore more satiety.", 18, 5, "H0"),
            Node("H2", "Medical devices", "Health", "Treatment restores more health with the same medical supplies.", 18, 5, "H0"),
            Node("H3", "Recovery suite", "Health", "Rest and exercise improve recovery and fitness more efficiently.", 26, 8, "H1", "H2"),
            Node("S0", "Improved solar cells", "Solar", "Installed solar generation increases by 20%.", 12, 4),
            Node("S1", "Array expansion", "Solar", "Unlock two additional 7 kW panel installations.", 18, 5, "S0"),
            Node("S2", "Dust servicing", "Solar", "A powered cleaning system removes deposits when surplus power exists.", 18, 5, "S0"),
            Node("S3", "Tracking array", "Solar", "Solar collection improves by 20%, with a 0.12 kW actuator load.", 26, 8, "S1", "S2"),
            Node("P0", "Environmental monitoring", "Protection", "Earlier warnings and clear environmental forecast details.", 12, 4),
            Node("P1", "Shielded shelter", "Protection", "Shelter's passive modeled dose transmission is reduced by half.", 18, 5, "P0"),
            Node("P2", "Thermal insulation", "Protection", "Habitat heat loss falls by 30%.", 18, 5, "P0"),
            Node("P3", "Resilient habitat", "Protection", "Slower equipment wear and reduced modeled exposure.", 26, 8, "P1", "P2"),
            Node("E0", "Battery modules", "Efficiency", "Unlock two additional 90 kWh battery installations.", 12, 4),
            Node("E1", "Deep storage", "Efficiency", "Storage capacity increases by 50% and discharge limit by 25%.", 18, 5, "E0"),
            Node("E2", "Efficient life support", "Efficiency", "Life control needs 25% less power per unit of useful output.", 18, 5, "E0"),
            Node("E3", "Smart distribution", "Efficiency", "Charging losses are lower and demand forecasts improve; knob settings stay yours.", 26, 8, "E1", "E2")
        };

        public static ResearchNode Find(string id)
        {
            foreach (ResearchNode node in Nodes)
                if (string.Equals(node.id, id, StringComparison.OrdinalIgnoreCase)) return node;
            return null;
        }

        private static ResearchNode Node(string id, string title, string branch, string description,
            int points, double parts, params string[] prerequisites)
        {
            return new ResearchNode { id = id, title = title, branch = branch, description = description,
                points = points, parts = parts, installationSeconds = 180, prerequisites = prerequisites };
        }
    }
}
