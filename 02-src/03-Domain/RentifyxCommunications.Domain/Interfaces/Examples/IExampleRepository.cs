using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Filters.Examples;
using RentifyxCommunications.Domain.Interfaces.Common;

namespace RentifyxCommunications.Domain.Interfaces.Examples;

public interface IExampleRepository :
    IAddRepository<ExampleEntity>,
    IGetByIdRepository<ExampleEntity>,
    IGetAllRepository<ExampleEntity, ExampleFilter>,
    IUpdateRepository<ExampleEntity>,
    IDeleteRepository<ExampleEntity>
{
}
