using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestApi.Data;
using TestApi.External;

namespace TestApi.Controllers;

[ApiController]
[Authorize]
[Route("api/products")]
public sealed class ProductsController(
    TestApiDbContext database,
    IExternalCatalogClient externalCatalog,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> List(
        CancellationToken cancellationToken)
    {
        var products = await database.Products
            .AsNoTracking()
            .OrderBy(product => product.Id)
            .Select(product => new ProductResponse(product.Id, product.Name, product.Price))
            .ToListAsync(cancellationToken);

        return Ok(products);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var product = await database.Products
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new ProductResponse(item.Id, item.Name, item.Price))
            .SingleOrDefaultAsync(cancellationToken);

        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = new Models.Product
        {
            Name = request.Name,
            Price = request.Price
        };
        database.Products.Add(product);
        await database.SaveChangesAsync(cancellationToken);

        var response = new ProductResponse(product.Id, product.Name, product.Price);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, response);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductResponse>> Update(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await database.Products.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        product.Name = request.Name;
        product.Price = request.Price;
        await database.SaveChangesAsync(cancellationToken);

        return Ok(new ProductResponse(product.Id, product.Name, product.Price));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var product = await database.Products.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        database.Products.Remove(product);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("import/{externalId:int}")]
    public async Task<ActionResult<ProductResponse>> Import(
        int externalId,
        CancellationToken cancellationToken)
    {
        ExternalCatalogProduct? externalProduct;
        try
        {
            externalProduct = await externalCatalog.GetProductAsync(externalId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        if (externalProduct is null)
        {
            return NotFound();
        }

        if (await database.Products.AnyAsync(product => product.Id == externalProduct.Id, cancellationToken))
        {
            return Conflict();
        }

        var product = new Models.Product
        {
            Id = externalProduct.Id,
            Name = externalProduct.Name,
            Price = externalProduct.Price
        };
        database.Products.Add(product);
        await database.SaveChangesAsync(cancellationToken);

        var response = new ProductResponse(product.Id, product.Name, product.Price);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, response);
    }

    [HttpPost("sync")]
    public async Task<ActionResult<CatalogSyncResponse>> Sync(
        SyncCatalogRequest request,
        CancellationToken cancellationToken)
    {
        var syncRun = new Models.CatalogSyncRun
        {
            Category = request.Category,
            StartedAt = timeProvider.GetUtcNow(),
            Status = Models.CatalogSyncStatus.Running
        };
        database.CatalogSyncRuns.Add(syncRun);
        await database.SaveChangesAsync(cancellationToken);

        IReadOnlyList<ExternalCatalogProduct> externalProducts;
        try
        {
            externalProducts = await externalCatalog.GetProductsAsync(
                request.Category,
                request.Limit,
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            syncRun.Status = Models.CatalogSyncStatus.Failed;
            syncRun.CompletedAt = timeProvider.GetUtcNow();
            syncRun.FailureReason = "The external catalog request failed.";
            await database.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        var productsById = externalProducts
            .Where(product => product.Id > 0)
            .GroupBy(product => product.Id)
            .ToDictionary(group => group.Key, group => group.Last());
        var externalIds = productsById.Keys.ToArray();
        var existingProducts = await database.Products
            .Where(product => externalIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        foreach (var externalProduct in productsById.Values)
        {
            if (existingProducts.TryGetValue(externalProduct.Id, out var existingProduct))
            {
                existingProduct.Name = externalProduct.Name;
                existingProduct.Price = externalProduct.Price;
                syncRun.UpdatedCount++;
                continue;
            }

            database.Products.Add(new Models.Product
            {
                Id = externalProduct.Id,
                Name = externalProduct.Name,
                Price = externalProduct.Price
            });
            syncRun.CreatedCount++;
        }

        syncRun.Status = Models.CatalogSyncStatus.Completed;
        syncRun.CompletedAt = timeProvider.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken);

        return Ok(new CatalogSyncResponse(
            syncRun.Id,
            syncRun.Status.ToString(),
            syncRun.CreatedCount,
            syncRun.UpdatedCount));
    }

    public sealed record ProductResponse(int Id, string Name, decimal Price);

    public sealed record CreateProductRequest(
        [Required, StringLength(200)] string Name,
        [Range(0.01, 1_000_000)] decimal Price);

    public sealed record UpdateProductRequest(
        [Required, StringLength(200)] string Name,
        [Range(0.01, 1_000_000)] decimal Price);

    public sealed record SyncCatalogRequest(
        [Required, StringLength(100)] string Category,
        [Range(1, 100)] int Limit = 25);

    public sealed record CatalogSyncResponse(
        long SyncRunId,
        string Status,
        int CreatedCount,
        int UpdatedCount);
}
