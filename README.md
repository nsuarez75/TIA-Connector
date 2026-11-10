# TIA-Connector

Conexión común con TIA Portal (Openness) para todas nuestras apps. Openness V19 solo funciona en .NET Framework 4.8,
así que el acceso a TIA vive aquí, en un **puente** 4.8 que cada app lanza y con el que habla mediante una librería
**cliente** que vale para .NET 10 y para .NET Framework 4.8.

Estado: especificación. Ver [`docs/especificacion.md`](docs/especificacion.md).
