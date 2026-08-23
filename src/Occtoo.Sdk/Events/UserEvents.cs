using CSharpFunctionalExtensions;

namespace Occtoo.Events;

/// <summary>Any <c>user.*</c> event, for a tenant user.</summary>
public abstract record UserEvent(
    UserId UserId,
    Maybe<string> Email,
    Maybe<string> FirstName,
    Maybe<string> LastName) : CloudEvent, IFilterableByUser;

/// <summary>A tenant user was created.</summary>
public sealed record UserCreated(
    UserId UserId,
    Maybe<string> Email,
    Maybe<string> FirstName,
    Maybe<string> LastName) : UserEvent(UserId, Email, FirstName, LastName);

/// <summary>A tenant user changed.</summary>
public sealed record UserUpdated(
    UserId UserId,
    Maybe<string> Email,
    Maybe<string> FirstName,
    Maybe<string> LastName) : UserEvent(UserId, Email, FirstName, LastName);

/// <summary>A tenant user was deleted.</summary>
public sealed record UserDeleted(
    UserId UserId,
    Maybe<string> Email,
    Maybe<string> FirstName,
    Maybe<string> LastName) : UserEvent(UserId, Email, FirstName, LastName);
