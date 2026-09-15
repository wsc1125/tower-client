using UnityEngine;

public sealed class InventoryHolder : MonoBehaviour
{
    private Inventory model;

    public Inventory Model => model ??= new Inventory();
}
