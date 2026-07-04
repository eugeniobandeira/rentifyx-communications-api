# Guide: Adding a New Feature to the Template

Use the `Example` feature as the reference implementation.

## 1. Domain

- Add `<Feature>Entity.cs` under `03-Domain/.../Entities/`
- Add `<Feature>ErrorCodes.cs` under `03-Domain/.../Constants/`
- Add `<Feature>Filter.cs` under `03-Domain/.../Filters/` — encapsulates pagination and search criteria for GetAll
- Add `I<Feature>Repository.cs` under `03-Domain/.../Interfaces/<Feature>/` — composite interface combining `IAddRepository<T>`, `IGetByIdRepository<T>`, `IGetAllRepository<T, TFilter>`, `IUpdateRepository<T>`, `IDeleteRepository<T>` as needed
- Add resource keys to `ValidationMessageResource.resx` and `ValidationMessageResource.pt-BR.resx`
- Regenerate `ValidationMessageResource.Designer.cs`

## 2. Application

Create one subfolder per operation under `02-Application/.../Features/<Feature>/Handlers/<Operation>/`:

```
Handlers/
  Create/
    CreateFeatureHandler.cs
    ICreateFeatureHandler.cs
    Request/CreateFeatureRequest.cs
    Validator/CreateFeatureValidator.cs
  Delete/
  GetAll/
  GetById/
  Update/
    UpdateFeatureCommand.cs
    ...
Mapper/FeatureMapper.cs    # CreateFeature, UpdateFeature, ToResponse, ToFilter
FeatureResponse.cs
```

## 3. Infrastructure

- Add `<Feature>Repository.cs` under `05-Infrastructure/.../Repositories/` implementing `I<Feature>Repository`

## 4. IoC

- Register the handler interfaces in `ApplicationDependencyInjection.cs`
- Register the repository in `InfrastructureDependencyInjection.cs`

## 5. Api

- Add one file per operation under `01-Api/.../Endpoints/<Feature>/`
- Register the tag in `Endpoints/Tags.cs`

## 6. Tests

| Project | What to add |
|---|---|
| Tests.Common | `<Feature>Builder.cs` |
| Tests.Validators | `Create<Feature>ValidatorTests.cs`, `Update<Feature>ValidatorTests.cs` |
| Tests.Handlers | One file per handler operation |
| Tests.Repositories | `<Feature>RepositoryTests.cs` |
| Tests.Integration | `<Feature>EndpointTests.cs` |

## 7. Local Testing

```bash
dotnet pack
dotnet new install ./clean-arch-template.csproj
dotnet new clean-arch -n MyApp
```
