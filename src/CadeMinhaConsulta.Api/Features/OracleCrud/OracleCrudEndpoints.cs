using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;

namespace CadeMinhaConsulta.Api.Features.OracleCrud;

public sealed record UnidadeInput(string Nome, string Cnes, string Cidade, string Uf);
public sealed record UnidadeDto(int Id, string Nome, string Cnes, string Cidade, string Uf, DateTime CriadoEm);

public sealed record EspecialidadeInput(string Nome, string CodigoSigtap, decimal ValorSigtap, int IdUnidade);
public sealed record EspecialidadeDto(int Id, string Nome, string CodigoSigtap, decimal ValorSigtap, int IdUnidade, DateTime CriadoEm);

internal static class OracleCrudEndpoints
{
    public static void MapOracleCrudEndpoints(this WebApplication app)
    {
        var unidades = app.MapGroup("/api/unidades").WithTags("Unidades de Saúde (Oracle CRUD)");

        unidades.MapGet("", async (IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("Oracle");
            if (string.IsNullOrWhiteSpace(connStr))
                return Results.Problem("Configure ConnectionStrings:Oracle.", statusCode: 503);

            await EnsureTablesAsync(connStr);
            var list = new List<UnidadeDto>();
            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new OracleCommand(
                "SELECT ID_UNIDADE, NM_UNIDADE, CD_CNES, NM_CIDADE, SG_UF, DT_CRIACAO FROM TB_CMC_UNIDADE ORDER BY ID_UNIDADE", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new UnidadeDto(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetDateTime(5)));
            }
            return Results.Ok(list);
        }).WithSummary("Lista todas as unidades de saúde cadastradas no Oracle (Read).").AllowAnonymous();

        unidades.MapGet("/{id:int}", async (int id, IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("Oracle");
            if (string.IsNullOrWhiteSpace(connStr))
                return Results.Problem("Configure ConnectionStrings:Oracle.", statusCode: 503);

            await EnsureTablesAsync(connStr);
            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new OracleCommand(
                "SELECT ID_UNIDADE, NM_UNIDADE, CD_CNES, NM_CIDADE, SG_UF, DT_CRIACAO FROM TB_CMC_UNIDADE WHERE ID_UNIDADE = :id", conn);
            cmd.Parameters.Add(new OracleParameter("id", id));
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return Results.NotFound(new { message = "Unidade não encontrada." });

            return Results.Ok(new UnidadeDto(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetDateTime(5)));
        }).WithSummary("Consulta uma unidade de saúde por ID no Oracle (Read).").AllowAnonymous();

        unidades.MapPost("", async ([FromBody] UnidadeInput input, IConfiguration config) =>
        {
            if (string.IsNullOrWhiteSpace(input.Nome) || string.IsNullOrWhiteSpace(input.Cnes))
                return Results.BadRequest(new { message = "Nome e CNES são obrigatórios." });

            var connStr = config.GetConnectionString("Oracle");
            if (string.IsNullOrWhiteSpace(connStr))
                return Results.Problem("Configure ConnectionStrings:Oracle.", statusCode: 503);

            await EnsureTablesAsync(connStr);
            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();

            await using var nextIdCmd = new OracleCommand("SELECT NVL(MAX(ID_UNIDADE), 0) + 1 FROM TB_CMC_UNIDADE", conn);
            var nextId = Convert.ToInt32(await nextIdCmd.ExecuteScalarAsync());
            var now = DateTime.UtcNow;

            await using var cmd = new OracleCommand(
                "INSERT INTO TB_CMC_UNIDADE (ID_UNIDADE, NM_UNIDADE, CD_CNES, NM_CIDADE, SG_UF, DT_CRIACAO) VALUES (:id, :nome, :cnes, :cidade, :uf, :dt)", conn);
            cmd.Parameters.Add(new OracleParameter("id", nextId));
            cmd.Parameters.Add(new OracleParameter("nome", input.Nome.Trim()));
            cmd.Parameters.Add(new OracleParameter("cnes", input.Cnes.Trim()));
            cmd.Parameters.Add(new OracleParameter("cidade", input.Cidade.Trim()));
            cmd.Parameters.Add(new OracleParameter("uf", input.Uf.Trim().ToUpperInvariant()));
            cmd.Parameters.Add(new OracleParameter("dt", now));
            await cmd.ExecuteNonQueryAsync();

            var created = new UnidadeDto(nextId, input.Nome.Trim(), input.Cnes.Trim(), input.Cidade.Trim(), input.Uf.Trim().ToUpperInvariant(), now);
            return Results.Created($"/api/unidades/{nextId}", created);
        }).WithSummary("Cadastra uma nova unidade de saúde no Oracle (Create).").AllowAnonymous();

        unidades.MapPut("/{id:int}", async (int id, [FromBody] UnidadeInput input, IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("Oracle");
            if (string.IsNullOrWhiteSpace(connStr))
                return Results.Problem("Configure ConnectionStrings:Oracle.", statusCode: 503);

            await EnsureTablesAsync(connStr);
            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new OracleCommand(
                "UPDATE TB_CMC_UNIDADE SET NM_UNIDADE = :nome, CD_CNES = :cnes, NM_CIDADE = :cidade, SG_UF = :uf WHERE ID_UNIDADE = :id", conn);
            cmd.Parameters.Add(new OracleParameter("nome", input.Nome.Trim()));
            cmd.Parameters.Add(new OracleParameter("cnes", input.Cnes.Trim()));
            cmd.Parameters.Add(new OracleParameter("cidade", input.Cidade.Trim()));
            cmd.Parameters.Add(new OracleParameter("uf", input.Uf.Trim().ToUpperInvariant()));
            cmd.Parameters.Add(new OracleParameter("id", id));

            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
                return Results.NotFound(new { message = "Unidade não encontrada." });

            return Results.Ok(new UnidadeDto(id, input.Nome.Trim(), input.Cnes.Trim(), input.Cidade.Trim(), input.Uf.Trim().ToUpperInvariant(), DateTime.UtcNow));
        }).WithSummary("Atualiza uma unidade de saúde existente no Oracle (Update).").AllowAnonymous();

        unidades.MapDelete("/{id:int}", async (int id, IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("Oracle");
            if (string.IsNullOrWhiteSpace(connStr))
                return Results.Problem("Configure ConnectionStrings:Oracle.", statusCode: 503);

            await EnsureTablesAsync(connStr);
            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new OracleCommand("DELETE FROM TB_CMC_UNIDADE WHERE ID_UNIDADE = :id", conn);
            cmd.Parameters.Add(new OracleParameter("id", id));

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows == 0 ? Results.NotFound(new { message = "Unidade não encontrada." }) : Results.NoContent();
        }).WithSummary("Remove uma unidade de saúde do Oracle (Delete).").AllowAnonymous();

        var especialidades = app.MapGroup("/api/especialidades").WithTags("Especialidades SUS (Oracle CRUD)");

        especialidades.MapGet("", async (IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("Oracle");
            if (string.IsNullOrWhiteSpace(connStr))
                return Results.Problem("Configure ConnectionStrings:Oracle.", statusCode: 503);

            await EnsureTablesAsync(connStr);
            var list = new List<EspecialidadeDto>();
            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new OracleCommand(
                "SELECT ID_ESPECIALIDADE, NM_ESPECIALIDADE, CD_SIGTAP, VL_SIGTAP, ID_UNIDADE, DT_CRIACAO FROM TB_CMC_ESPECIALIDADE ORDER BY ID_ESPECIALIDADE", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new EspecialidadeDto(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                    reader.GetDecimal(3), reader.GetInt32(4), reader.GetDateTime(5)));
            }
            return Results.Ok(list);
        }).WithSummary("Lista todas as especialidades cadastradas no Oracle (Read).").AllowAnonymous();

        especialidades.MapGet("/{id:int}", async (int id, IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("Oracle");
            if (string.IsNullOrWhiteSpace(connStr))
                return Results.Problem("Configure ConnectionStrings:Oracle.", statusCode: 503);

            await EnsureTablesAsync(connStr);
            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new OracleCommand(
                "SELECT ID_ESPECIALIDADE, NM_ESPECIALIDADE, CD_SIGTAP, VL_SIGTAP, ID_UNIDADE, DT_CRIACAO FROM TB_CMC_ESPECIALIDADE WHERE ID_ESPECIALIDADE = :id", conn);
            cmd.Parameters.Add(new OracleParameter("id", id));
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return Results.NotFound(new { message = "Especialidade não encontrada." });

            return Results.Ok(new EspecialidadeDto(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetInt32(4), reader.GetDateTime(5)));
        }).WithSummary("Consulta uma especialidade por ID no Oracle (Read).").AllowAnonymous();

        especialidades.MapPost("", async ([FromBody] EspecialidadeInput input, IConfiguration config) =>
        {
            if (string.IsNullOrWhiteSpace(input.Nome) || string.IsNullOrWhiteSpace(input.CodigoSigtap))
                return Results.BadRequest(new { message = "Nome e Código SIGTAP são obrigatórios." });

            var connStr = config.GetConnectionString("Oracle");
            if (string.IsNullOrWhiteSpace(connStr))
                return Results.Problem("Configure ConnectionStrings:Oracle.", statusCode: 503);

            await EnsureTablesAsync(connStr);
            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();

            await using var nextIdCmd = new OracleCommand("SELECT NVL(MAX(ID_ESPECIALIDADE), 0) + 1 FROM TB_CMC_ESPECIALIDADE", conn);
            var nextId = Convert.ToInt32(await nextIdCmd.ExecuteScalarAsync());
            var now = DateTime.UtcNow;

            await using var cmd = new OracleCommand(
                "INSERT INTO TB_CMC_ESPECIALIDADE (ID_ESPECIALIDADE, NM_ESPECIALIDADE, CD_SIGTAP, VL_SIGTAP, ID_UNIDADE, DT_CRIACAO) VALUES (:id, :nome, :sigtap, :valor, :unidade, :dt)", conn);
            cmd.Parameters.Add(new OracleParameter("id", nextId));
            cmd.Parameters.Add(new OracleParameter("nome", input.Nome.Trim()));
            cmd.Parameters.Add(new OracleParameter("sigtap", input.CodigoSigtap.Trim()));
            cmd.Parameters.Add(new OracleParameter("valor", input.ValorSigtap));
            cmd.Parameters.Add(new OracleParameter("unidade", input.IdUnidade));
            cmd.Parameters.Add(new OracleParameter("dt", now));
            await cmd.ExecuteNonQueryAsync();

            var created = new EspecialidadeDto(nextId, input.Nome.Trim(), input.CodigoSigtap.Trim(), input.ValorSigtap, input.IdUnidade, now);
            return Results.Created($"/api/especialidades/{nextId}", created);
        }).WithSummary("Cadastra uma nova especialidade no Oracle (Create).").AllowAnonymous();

        especialidades.MapPut("/{id:int}", async (int id, [FromBody] EspecialidadeInput input, IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("Oracle");
            if (string.IsNullOrWhiteSpace(connStr))
                return Results.Problem("Configure ConnectionStrings:Oracle.", statusCode: 503);

            await EnsureTablesAsync(connStr);
            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new OracleCommand(
                "UPDATE TB_CMC_ESPECIALIDADE SET NM_ESPECIALIDADE = :nome, CD_SIGTAP = :sigtap, VL_SIGTAP = :valor, ID_UNIDADE = :unidade WHERE ID_ESPECIALIDADE = :id", conn);
            cmd.Parameters.Add(new OracleParameter("nome", input.Nome.Trim()));
            cmd.Parameters.Add(new OracleParameter("sigtap", input.CodigoSigtap.Trim()));
            cmd.Parameters.Add(new OracleParameter("valor", input.ValorSigtap));
            cmd.Parameters.Add(new OracleParameter("unidade", input.IdUnidade));
            cmd.Parameters.Add(new OracleParameter("id", id));

            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
                return Results.NotFound(new { message = "Especialidade não encontrada." });

            return Results.Ok(new EspecialidadeDto(id, input.Nome.Trim(), input.CodigoSigtap.Trim(), input.ValorSigtap, input.IdUnidade, DateTime.UtcNow));
        }).WithSummary("Atualiza uma especialidade existente no Oracle (Update).").AllowAnonymous();

        especialidades.MapDelete("/{id:int}", async (int id, IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("Oracle");
            if (string.IsNullOrWhiteSpace(connStr))
                return Results.Problem("Configure ConnectionStrings:Oracle.", statusCode: 503);

            await EnsureTablesAsync(connStr);
            await using var conn = new OracleConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new OracleCommand("DELETE FROM TB_CMC_ESPECIALIDADE WHERE ID_ESPECIALIDADE = :id", conn);
            cmd.Parameters.Add(new OracleParameter("id", id));

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows == 0 ? Results.NotFound(new { message = "Especialidade não encontrada." }) : Results.NoContent();
        }).WithSummary("Remove uma especialidade do Oracle (Delete).").AllowAnonymous();
    }

    private static async Task EnsureTablesAsync(string connStr)
    {
        await using var conn = new OracleConnection(connStr);
        await conn.OpenAsync();

        const string createUnidade = """
            BEGIN
                EXECUTE IMMEDIATE 'CREATE TABLE TB_CMC_UNIDADE (
                    ID_UNIDADE NUMBER(10) PRIMARY KEY,
                    NM_UNIDADE VARCHAR2(120) NOT NULL,
                    CD_CNES VARCHAR2(20) NOT NULL,
                    NM_CIDADE VARCHAR2(80) NOT NULL,
                    SG_UF CHAR(2) NOT NULL,
                    DT_CRIACAO DATE DEFAULT SYSDATE NOT NULL
                )';
            EXCEPTION
                WHEN OTHERS THEN
                    IF SQLCODE != -955 THEN RAISE; END IF;
            END;
            """;

        const string createEspecialidade = """
            BEGIN
                EXECUTE IMMEDIATE 'CREATE TABLE TB_CMC_ESPECIALIDADE (
                    ID_ESPECIALIDADE NUMBER(10) PRIMARY KEY,
                    NM_ESPECIALIDADE VARCHAR2(100) NOT NULL,
                    CD_SIGTAP VARCHAR2(20) NOT NULL,
                    VL_SIGTAP NUMBER(10,2) NOT NULL,
                    ID_UNIDADE NUMBER(10) NOT NULL,
                    DT_CRIACAO DATE DEFAULT SYSDATE NOT NULL,
                    CONSTRAINT FK_CMC_ESP_UNIDADE FOREIGN KEY (ID_UNIDADE) REFERENCES TB_CMC_UNIDADE(ID_UNIDADE)
                )';
            EXCEPTION
                WHEN OTHERS THEN
                    IF SQLCODE != -955 THEN RAISE; END IF;
            END;
            """;

        await using (var cmd1 = new OracleCommand(createUnidade, conn))
            await cmd1.ExecuteNonQueryAsync();
        await using (var cmd2 = new OracleCommand(createEspecialidade, conn))
            await cmd2.ExecuteNonQueryAsync();
    }
}