namespace HotelManagement.Web.Data;

/// <summary>Demo room catalogue. Recovered from the original HMSDB1 backup (same rooms, same prices).</summary>
public sealed record SeedRoom(string Id, string Type, int Price, string Description, string Picture, string Complimentary, string Features);

public static class SeedData
{
    public static readonly SeedRoom[] Rooms =
    {
        new SeedRoom("r101", "Presidential Suite", 22000, "Attractively ornamented with complete marble & elegant wood fittings.", "1.jpg", "Breakfast for 4 pax. Welcome drink (on arrival)", "2 Presidential suites. King size bed."),
        new SeedRoom("r102", "Royal Suite", 15000, "Attractively ornamented with complete marble & elegant wood fittings.", "2.jpg", "Breakfast for 4 pax. Welcome drink (on arrival)", "2 Presidential suites. King size bed. LED TV. Mini Fridge."),
        new SeedRoom("r103", "Premier Suite", 11000, "Attractively ornamented with complete marble & elegant wood fittings.", "3.jpg", "Breakfast for 4 pax. Welcome drink (on arrival)", "2 Presidential suites. King size bed. LED TV. Mini Fridge. Living area."),
        new SeedRoom("r104", "Honeymoon Suite", 17500, "Attractively ornamented with complete marble & elegant wood fittings.", "4.jpg", "Breakfast for 4 pax. Welcome drink (on arrival)", "2 Presidential suites. King size bed. LED TV. Mini Fridge. Living area."),
        new SeedRoom("r105", "Family Suite", 19000, "Attractively ornamented with complete marble & elegant wood fittings.", "5.jpg", "Breakfast for 4 pax. Welcome drink (on arrival)", "2 Presidential suites. King size bed. LED TV. Mini Fridge. Living area. Living room"),
        new SeedRoom("r106", "Deluxe Room", 9000, "Attractively ornamented with complete marble & elegant wood fittings.", "6.jpg", "Breakfast for 2 pax. Airport pickup.", "King size bed. LED TV. Mini Fridge. Living area. Living room."),
        new SeedRoom("r107", "Executive Suite", 2200, "Elegantly furnished executive space with additional office/work area.", "7.jpg", "Breakfast for 2 pax. Airport pickup. Evening snacks. Fruit basket.", "Living area. Executive desk."),
        new SeedRoom("r108", "Single Room", 6000, "Cozy room suitable for solo-travelers.", "8.jpg", "Mineral water. 1 bottle. Free Wi-Fi.", "Single bed. 24\" LED TV. Cable service."),
        new SeedRoom("r109", "Double Room", 8500, "Comfortable room for two with modern amenities.", "9.jpg", "Mineral water 2 bottles. Free Wi-Fi.", "Queen bed. Hardrobe. Flat screen TV."),
        new SeedRoom("r110", "Luxury Suite", 26000, "Spacious luxury suite with opulent interiors.", "10.jpg", "Breakfast for 4 pax. Airport pickup. Complimentary laundry. Welcome drink.", "King bedroom. Dining space."),
        new SeedRoom("r111", "Garden View Suite", 15500, "Overlooks beautiful gardens and decorated with garden theme.", "11.jpg", "Breakfast. Welcome drink. Nature walk pass. Free Wi-Fi.", "Garden view balcony. Twin bed."),
        new SeedRoom("r112", "Studio Room", 12005, "Open-plan room with compact luxury features.", "12.jpg", "Breakfast. Free internet. Water bottles. Welcome drink.", "Open layout. Kitchenette. Seating area."),
        new SeedRoom("r113", "City View Room", 10500, "Panoramic city view with modern furnishing.", "13.jpg", "Breakfast. Wi-Fi. Rooftop access.", "Large windows. Queen bed."),
        new SeedRoom("r114", "Twin Deluxe", 9800, "Two separate beds with plush interiors.", "14.jpg", "Breakfast for 2 pax. Wi-Fi. Bottled water.", "Twin beds. Writing desk. Balcony."),
    };
}
