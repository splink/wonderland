using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Names for the places of the world, fixed by where they stand (so the same station is always "Brookside", across
    /// sessions and after a reload): stations name the villages around them.
    /// </summary>
    public static class PlaceNames
    {
        private static readonly string[] Villages =
        {
            "Brookside", "Hilltown", "Millford", "Ashby", "Elmstead", "Kingsbridge", "Oakham", "Fernley", "Rosedale", "Thornbury",
            "Willowmere", "Larkhill", "Applecross", "Bramblewood", "Cherryford", "Dunmore", "Foxhollow", "Greenfold", "Hazelbank",
            "Juniper Bay", "Kestrel Ford", "Little Basing", "Meadowcroft", "Nettlecombe", "Orchard End", "Pebblewick", "Quarry Hill",
            "Rushmere", "Sunnydale", "Tumbledown", "Upper Wold", "Violet Green",
        };

        private static uint Hash(Vector2 p)
        {
            uint h = (uint)Mathf.RoundToInt(p.x * 1000f) * 73856093u ^ (uint)Mathf.RoundToInt(p.y * 1000f) * 19349663u;
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }

        /// <summary>The village a station (by where it stands) is named after.</summary>
        public static string Village(Vector2 at) => Villages[Hash(at) % (uint)Villages.Length];
    }
}
