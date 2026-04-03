using System;

namespace JellyfinBookReader.Dto;

/// <summary>
/// Instruction queued to <see cref="Services.WarmUpBackgroundService"/> asking it to
/// pre-extract a contiguous range of pages for the given book.
///
/// Published by the controller on session start (pages 0..WarmUpInitialPages)
/// and on each cache miss (pages n+1..n+WarmUpPrefetchWindow).
/// </summary>
public record WarmUpRequest(
    Guid BookId,
    string FilePath,
    int StartPage,
    int PageCount);
