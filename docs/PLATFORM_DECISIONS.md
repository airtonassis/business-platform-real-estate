# entidades
Entity
AggregateRoot
AuditableEntity
TenantEntity

# interfaces
IRepository
IUnitOfWork
IDomainEvent
IDomainService

# Objetos Compartilhados
Result<T>
Error
Pagination
Specification

| Tema            | Decisão                            |
| --------------- | ---------------------------------- |
| ORM             | Entity Framework Core              |
| Banco           | PostgreSQL                         |
| Cache           | Redis                              |
| Mensageria      | RabbitMQ                           |
| API             | REST                               |
| Autenticação    | JWT + Keycloak (ou outro provedor) |
| Logs            | Serilog                            |
| Observabilidade | OpenTelemetry                      |
| Documentação    | Swagger/OpenAPI                    |
| Testes          | xUnit                              |
| Arquitetura     | Clean Architecture + DDD           |
| Multi-tenancy   | TenantId obrigatório               |
