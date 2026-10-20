using DatabaseSync.Data;
using DatabaseSync.Models;

namespace DatabaseSync.Tests;

public sealed class SqlBuilderTests
{
    private static readonly TableSchema Schema = new("Produit",
    [
        new("Id", "int", "int unsigned", 1, false, true, true),
        new("Nom", "varchar", "varchar(100)", 2, false, false, false),
        new("Prix", "decimal", "decimal(10,2)", 3, true, false, false)
    ]);

    [Fact]
    public void UpsertUsesInsertOnDuplicateKeyAndNeverDestructiveSql()
    {
        var sql = SqlBuilder.BuildUpsert(Schema, 2);

        Assert.StartsWith("INSERT INTO `Produit`", sql);
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql);
        Assert.Contains("`Nom` = VALUES(`Nom`)", sql);
        Assert.DoesNotContain("REPLACE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpsertDoesNotUpdatePrimaryOrAutoIncrementColumns()
    {
        var update = SqlBuilder.BuildUpsert(Schema, 1).Split("UPDATE ", 2)[1];
        Assert.DoesNotContain("`Id` =", update);
        Assert.Contains("`Prix` =", update);
    }

    [Fact]
    public void SelectOrdersByPrimaryKey()
    {
        Assert.Equal("SELECT `Id`, `Nom`, `Prix` FROM `Produit` ORDER BY `Id`", SqlBuilder.BuildSelect(Schema));
    }

    [Fact]
    public void IdentifierEscapesBackticks()
    {
        Assert.Equal("`odd``name`", SqlBuilder.QuoteIdentifier("odd`name"));
    }
}
