using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// The library's side of the catalog: every object belongs to a family within its category (Homes: Cottages, Town
    /// homes...), has a sentence about what it is, and says where it has to go to work. The library panel shows a
    /// family at a time as tiles with little models, and what the object pointed at does in the world.
    /// </summary>
    public static partial class Catalog
    {
        /// <summary>The families of a category, in the order the library shows them.</summary>
        public static List<string> Families(PlaceCategory c)
        {
            var list = new List<string>();
            foreach (var b in All) if (b.Category == c && b.Family != null && !list.Contains(b.Family)) list.Add(b.Family);
            return list;
        }

        public static List<int> InFamily(PlaceCategory c, string family)
        {
            var list = new List<int>();
            foreach (var b in All) if (b.Category == c && b.Family == family) list.Add(b.Id);
            return list;
        }

        private static void Describe(List<Blueprint> list)
        {
            void D(int id, string family, string about, string requires = null) { var b = list[id]; b.Family = family; b.About = about; b.Requires = requires; }
            // Homes.
            D(Cottage, "Cottages", "A whitewashed cottage with a rose by the door - home to one villager");
            D(Thatched, "Cottages", "A long, low house under a deep straw roof - home to one villager");
            D(Chalet, "Cottages", "A timber mountain chalet with a wide balcony and a bench outside");
            D(Townhouse, "Town homes", "A narrow, tall town house - home to one villager");
            D(Apartments, "Town homes", "A block of flats over several floors - home to two households");
            // Town.
            D(TownHall, "Civic", "The seat of the village: a clock tower over a pillared front, the clerk at work inside");
            D(Church, "Civic", "A church with a tall spire and a clock - people gather on its steps");
            D(Museum, "Civic", "Halls of curiosities behind a columned portico, a dinosaur in the courtyard");
            D(Hotel, "Civic", "A grand hotel: guests stay and go sightseeing round the village", "near the shops and sights");
            D(Fountain, "Squares", "A fountain for the village square - people meet round its rim");
            D(Park, "Squares", "Lawns, a duck pond, paths and benches for a stroll");
            // Shops.
            D(Bakery, "Food", "Bread and cakes from before dawn; it takes the farms' produce", "by a street, for the delivery lorry");
            D(Grocer, "Food", "Fruit and vegetables out front; it takes produce and goods", "by a street, for the delivery lorry");
            D(Cafe, "Dining", "Coffee and cake at tables under parasols");
            D(Restaurant, "Dining", "Dinner on the terrace - busiest in the evening");
            D(IceCream, "Dining", "Ice cream on summer afternoons - children love it");
            D(Pub, "Dining", "Where the village gathers in the evening");
            D(FurnitureShop, "Goods", "Chairs and tables made from the sawmill's planks", "the sawmill's lorry brings the timber");
            D(PostOffice, "Goods", "Its van empties the letterboxes and delivers the post", "by a street, for the post van");
            // Work.
            D(Hospital, "Emergency", "Its ambulance answers when someone is taken ill", "by a street, for the ambulance");
            D(Police, "Emergency", "Its patrol car keeps watch and answers calls", "by a street, for the patrol car");
            D(FireStation, "Emergency", "The fire engine waits behind the big doors", "by a street, for the fire engine");
            D(School, "Emergency", "Lessons by day, a yard to play in at break");
            list[School].Family = "Learning";
            D(Factory, "Industry", "Saw-tooth roofs and a tall chimney: it makes goods for the shops and the goods trains", "by a street, for its lorry");
            D(Sawmill, "Industry", "Saws logs into planks; its lorry takes them to the furniture maker", "by a street, for its lorry");
            D(Studio, "Media", "Screens showing what the CCTV cameras see");
            // Farm.
            D(Barn, "Farm buildings", "A big red barn; its lorry takes the produce to the shops", "by a lane or street, for its lorry");
            D(Silo, "Farm buildings", "A tall grain silo");
            D(Windmill, "Farm buildings", "Its sails turn in the wind while the miller grinds the corn");
            D(Greenhouse, "Farm buildings", "Glasshouses full of tomatoes and seedlings");
            D(WheatField, "Fields", "A field of wheat; the farmer works it with a tractor");
            D(Garden, "Fields", "Rows of vegetables tended by the gardener");
            D(HayBales, "Fields", "Hay bales to sit on");
            // Leisure.
            D(Carousel, "Fairground", "Painted horses go round and round - visitors ride them");
            D(FerrisWheel, "Fairground", "A ride with a view over the whole world");
            D(Balloon, "Fairground", "Flights over the countryside by day");
            D(FairyCastle, "Landmarks", "A fairy-tale castle of white towers and blue spires - the jewel of the world");
            D(CastleRuin, "Landmarks", "A crumbling keep and broken walls to explore");
            D(Lookout, "Landmarks", "Climb the tower for the view");
            D(Football, "Sport", "A pitch: a match whenever there are players");
            D(Stadium, "Sport", "Matches at three and half past seven - the crowd fills the stands");
            D(Playground, "Sport", "Swings, a slide and a seesaw for the children");
            D(Disco, "Evenings", "Dancing after dark under the mirror ball");
            D(Bandstand, "Evenings", "A band plays and people gather round to listen");
            D(Campsite, "Outdoors", "Tents round a campfire");
            // Zoo: the old all-in-one zoo is the neighbourhood zoo.
            D(Zoo, "Small zoo", "A whole little zoo in one: giraffes, elephants, lions, zebras and penguins behind a gate");
            list[Zoo].Category = PlaceCategory.Zoo;
            // Travel.
            D(Station, "Railway", "A station on the platform's edge: trains stop and passengers get on and off", "beside the railway line");
            D(Halt, "Railway", "A little halt beside the line - trains call here too", "beside the railway line");
            D(FreightYard, "Railway", "Its crane loads and unloads goods trains; its lorry takes the cargo on", "beside the railway line, by a street");
            D(SignalBox, "Railway", "The signalman's box beside the line", "beside the railway line");
            D(WaterTower, "Railway", "Water for the steam engines");
            D(Fuel, "Road", "Pumps and a little shop for drivers", "by a road");
            D(TaxiRank, "Road", "Two taxis wait here for fares", "by a road");
            D(CarPark, "Road", "Visitors park here while they shop", "by a road, near the shops");
            D(Jetty, "Water", "Boat trips from its end", "at a pond's edge, its end over the water");
            D(RowingBoat, "Water", "Rows about the pond", "on the water");
            D(SailingBoat, "Water", "Sails with the wind", "on the water");
            D(Steamer, "Water", "Takes six on a trip round the lake", "on the water");
            D(Harbour, "Water", "Cranes load container ships that sail to other harbours", "on the shore of a big lake, front to the water");
            // Nature.
            D(Boulders, "Rocks", "A few mossy boulders");
            D(Crag, "Rocks", "A rocky outcrop to look out from");
            D(LogPile, "Woodland", "A pile of logs to sit on");
            // Details.
            D(Lamp, "Street", "A street lamp that lights up at dusk");
            D(Bench, "Street", "A bench to sit on");
            D(Letterbox, "Street", "The post van empties it on its round", "on the post van's round, by a street");
            D(Signpost, "Street", "Pointing the way");
            D(Zebra, "Street", "People cross the street here, cars stop for them", "across a street");
            D(Camera, "Street", "A CCTV camera: select it to see its picture and turn it");
            D(FlowerBed, "Garden", "A bed of flowers");
            D(Well, "Garden", "A wishing well - people gather round it");
            D(Statue, "Garden", "A statue on a plinth");
            D(Picnic, "Garden", "A picnic table for two");
            foreach (var b in list) { b.Family ??= "Other"; b.About ??= b.Hint ?? b.Name; }
        }

        /// <summary>What the library says about an object: what it is, and what it does in the world.</summary>
        public static string Info(int id)
        {
            var b = Get(id);
            var sb = new StringBuilder();
            sb.Append(b.About);
            var facts = new List<string>();
            if (b.Residents > 0) facts.Add(b.Residents == 1 ? "home to a villager" : $"home to {b.Residents}");
            if (b.Job != null) facts.Add($"a {b.Job} works here {Hour(b.ShiftFrom)}-{Hour(b.ShiftTo)}");
            if (b.Closes - b.Opens < 23.9f) facts.Add($"open {Hour(b.Opens)}-{Hour(b.Closes)}");
            string need = b.Serves switch { Need.Food => "people come to eat", Need.Errands => "people come shopping", Need.Social => "people meet here", Need.Fun => "people come for fun", Need.Sights => "people come to look", _ => null };
            if (need != null && !b.Dwelling) facts.Add(need + (b.Kids ? ", children love it" : ""));
            if (b.Makes != Cargo.None) facts.Add($"makes {Cargoes(b.Makes)}");
            if (b.Takes != Cargo.None) facts.Add($"takes {Cargoes(b.Takes)}");
            if (b.Lorries > 0) facts.Add(b.Lorries == 1 ? "a lorry based here" : $"{b.Lorries} lorries");
            if (b.Taxis > 0) facts.Add(b.Taxis == 1 ? "a taxi waits here" : $"{b.Taxis} taxis wait here");
            if (b.Base) facts.Add("an emergency vehicle is based here");
            if (facts.Count > 0) sb.Append("\n<color=#E8D7A8>").Append(string.Join(" · ", facts)).Append("</color>");
            if (b.Requires != null) sb.Append("\n<color=#9FD0FF>Put it ").Append(b.Requires).Append("</color>");
            return sb.ToString();
        }

        private static string Hour(float h) { h = Mathf.Repeat(h, 24f); int m = Mathf.RoundToInt(h * 60f); return $"{m / 60}:{m % 60:00}"; }

        private static string Cargoes(Cargo c)
        {
            var parts = new List<string>();
            if ((c & Cargo.Produce) != 0) parts.Add("produce");
            if ((c & Cargo.Goods) != 0) parts.Add("goods");
            if ((c & Cargo.Timber) != 0) parts.Add("timber");
            if ((c & Cargo.Mail) != 0) parts.Add("post");
            return string.Join(", ", parts);
        }
    }
}
