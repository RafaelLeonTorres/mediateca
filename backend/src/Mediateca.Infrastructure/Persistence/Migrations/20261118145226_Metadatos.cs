using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mediateca.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261118145226_Metadatos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // AniDB ya no se usa: su id no se convierte en la duración (EF lo propuso como cambio de nombre
        // porque las dos columnas son enteros), se borra y RuntimeMinutes se agrega aparte.
        migrationBuilder.DropColumn(
            name: "AniDbId",
            table: "MediaItems");

        migrationBuilder.AddColumn<int>(
            name: "RuntimeMinutes",
            table: "MediaItems",
            type: "integer",
            nullable: true);

        // Las cuatro listas (Actors, Directors, Studios, Tags) son NOT NULL: con DEFAULT '{}' las filas ya
        // escaneadas quedan con una lista vacía; sin él, PostgreSQL rechaza la migración en una base con datos.
        migrationBuilder.AddColumn<List<string>>(
            name: "Actors",
            table: "MediaItems",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'");

        migrationBuilder.AddColumn<int>(
            name: "AniListId",
            table: "MediaItems",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BackdropPath",
            table: "MediaItems",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<List<string>>(
            name: "Directors",
            table: "MediaItems",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'");

        migrationBuilder.AddColumn<int>(
            name: "EndYear",
            table: "MediaItems",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ExternalLinksJson",
            table: "MediaItems",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MetadataMessage",
            table: "MediaItems",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "MetadataUpdatedAt",
            table: "MediaItems",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NextAiringAt",
            table: "MediaItems",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "NextAiringEpisode",
            table: "MediaItems",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OriginalTitle",
            table: "MediaItems",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScanKey",
            table: "MediaItems",
            type: "character varying(600)",
            maxLength: 600,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "MediaItems",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

        migrationBuilder.AddColumn<List<string>>(
            name: "Studios",
            table: "MediaItems",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'");

        migrationBuilder.AddColumn<List<string>>(
            name: "Tags",
            table: "MediaItems",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'");

        migrationBuilder.CreateIndex(
            name: "IX_MediaItems_LibraryId_MetadataStatus",
            table: "MediaItems",
            columns: new[] { "LibraryId", "MetadataStatus" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_MediaItems_LibraryId_MetadataStatus",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "Actors",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "AniListId",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "BackdropPath",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "Directors",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "EndYear",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "ExternalLinksJson",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "MetadataMessage",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "MetadataUpdatedAt",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "NextAiringAt",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "NextAiringEpisode",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "OriginalTitle",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "ScanKey",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "Status",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "Studios",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "Tags",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "RuntimeMinutes",
            table: "MediaItems");

        migrationBuilder.AddColumn<int>(
            name: "AniDbId",
            table: "MediaItems",
            type: "integer",
            nullable: true);
    }
}
