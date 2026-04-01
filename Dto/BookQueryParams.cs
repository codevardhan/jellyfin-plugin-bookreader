using System;
using Microsoft.AspNetCore.Mvc;

namespace JellyfinBookReader.Dto;

/// <summary>
/// Query parameters for GET /api/BookReader/books.
/// Bound from the query string automatically by ASP.NET.
/// </summary>
public class BookQueryParams
{
    [FromQuery(Name = "search")]
    public string? Search { get; set; }

    [FromQuery(Name = "author")]
    public string? Author { get; set; }

    [FromQuery(Name = "genre")]
    public string? Genre { get; set; }

    [FromQuery(Name = "format")]
    public string? Format { get; set; }

    [FromQuery(Name = "status")]
    public string? Status { get; set; }

    [FromQuery(Name = "libraryId")]
    public Guid? LibraryId { get; set; }

    [FromQuery(Name = "sort")]
    public string Sort { get; set; } = "title";

    [FromQuery(Name = "sortOrder")]
    public string SortOrder { get; set; } = "asc";

    [FromQuery(Name = "limit")]
    public int? Limit { get; set; }

    [FromQuery(Name = "offset")]
    public int Offset { get; set; } = 0;
}