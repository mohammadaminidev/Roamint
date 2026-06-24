namespace Roamint.Receipts.Infrastructure.Tests;

/// <summary>
/// A representative Yerevan City online-order payload, anonymized and trimmed to the fields the parser reads
/// plus a few noise fields it must ignore. The line prices sum to 6787.04 and reconcile to
/// <c>totalPrice</c> 6787; it mixes weight items (<c>isKilogram: true</c>, e.g. Cherry 0.722 kg) with count
/// items (Eggs ×1, Patties ×2). Used by the parser tests.
/// </summary>
internal static class YerevanCitySample
{
    public const string Json = """
    {
      "success": true,
      "data": {
        "orderId": "AC0000122700",
        "createDate": "2026-06-21T14:32:11.000Z",
        "paymentMethod": 2,
        "totalPrice": 6787,
        "totalToPay": 6787,
        "deliveryPrice": 0,
        "usedBonusAmount": 0,
        "orderItems": [
          { "name": "Cherry",   "codeSap": "SKU-0001", "price": 1287.04, "quantity": 0.722, "isKilogram": true,  "weightMeasure": "G",  "bonus": 0, "visiblePrice": 1782 },
          { "name": "Eggs",     "codeSap": "SKU-0002", "price": 990,     "quantity": 1,     "isKilogram": false, "weightMeasure": "PCS","bonus": 0, "visiblePrice": 990 },
          { "name": "Patties",  "codeSap": "SKU-0003", "price": 1300,    "quantity": 2,     "isKilogram": false, "weightMeasure": "G",  "bonus": 0, "visiblePrice": 650 },
          { "name": "Milk",     "codeSap": "SKU-0004", "price": 450,     "quantity": 1,     "isKilogram": false, "weightMeasure": "ML", "bonus": 0, "visiblePrice": 450 },
          { "name": "Bread",    "codeSap": "SKU-0005", "price": 250,     "quantity": 1,     "isKilogram": false, "weightMeasure": "G",  "bonus": 0, "visiblePrice": 250 },
          { "name": "Bananas",  "codeSap": "SKU-0006", "price": 360,     "quantity": 0.5,   "isKilogram": true,  "weightMeasure": "G",  "bonus": 0, "visiblePrice": 720 },
          { "name": "Cheese",   "codeSap": "SKU-0007", "price": 900,     "quantity": 0.3,   "isKilogram": true,  "weightMeasure": "G",  "bonus": 0, "visiblePrice": 3000 },
          { "name": "Yogurt",   "codeSap": "SKU-0008", "price": 320,     "quantity": 1,     "isKilogram": false, "weightMeasure": "ML", "bonus": 0, "visiblePrice": 320 },
          { "name": "Water",    "codeSap": "SKU-0009", "price": 280,     "quantity": 1,     "isKilogram": false, "weightMeasure": "ML", "bonus": 0, "visiblePrice": 280 },
          { "name": "Tomatoes", "codeSap": "SKU-0010", "price": 650,     "quantity": 0.8,   "isKilogram": true,  "weightMeasure": "G",  "bonus": 0, "visiblePrice": 812.5 }
        ]
      }
    }
    """;
}
