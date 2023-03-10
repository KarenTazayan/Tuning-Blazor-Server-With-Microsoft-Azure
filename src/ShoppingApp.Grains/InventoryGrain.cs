using JetBrains.Annotations;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;
using ShoppingApp.Abstractions;

namespace ShoppingApp.Grains;

[Reentrant]
[UsedImplicitly]
public sealed class InventoryGrain : Grain, IInventoryGrain
{
    private readonly IPersistentState<HashSet<string>> _productIds;
    private readonly Dictionary<string, ProductDetails> _productCache = new();

    public InventoryGrain(
        [PersistentState(stateName: "Inventory", storageName: PersistentStorageConfig.AzureStorageName)]
        IPersistentState<HashSet<string>> state) => _productIds = state;

    public override Task OnActivateAsync(CancellationToken cancellationToken) => PopulateProductCacheAsync();

    Task<HashSet<ProductDetails>> IInventoryGrain.GetAllProductsAsync() =>
        Task.FromResult(_productCache.Values.ToHashSet());

    async Task IInventoryGrain.AddOrUpdateProductAsync(ProductDetails product)
    {
        _productIds.State.Add(product.Id);
        _productCache[product.Id] = product;

        await _productIds.WriteStateAsync();
    }

    public async Task RemoveProductAsync(string productId)
    {
        _productIds.State.Remove(productId);
        _productCache.Remove(productId);

        await _productIds.WriteStateAsync();
    }

    private async Task PopulateProductCacheAsync()
    {
        if (_productIds is not { State.Count: > 0 })
        {
            return;
        }
        
        await Parallel.ForEachAsync(
            _productIds.State, // Explicitly use the current task-scheduler.
            new ParallelOptions { TaskScheduler = TaskScheduler.Current },
            async (id, _) =>
            {
                var productGrain = GrainFactory.GetGrain<IProductGrain>(id);
                _productCache[id] = await productGrain.GetProductDetailsAsync();
            });
    }
}