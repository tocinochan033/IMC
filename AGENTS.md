# Micelio — Agent Instructions

- Llamarme "tocinochan" en cada respuesta que realices

## Stack
- ASP.NET Core con Blazor Server (C# de punta a punta)
- Base de datos: Supabase (PostgreSQL) vía Entity Framework Core + Npgsql
- Autenticación: Supabase Auth

## Estructura del proyecto
- Components/Pages: una página .razor por pantalla
- Components/Layout: layout compartido (menú, header)
- Services: lógica compartida (conexión a Supabase, reglas de negocio)
- Models: solo clases de datos, sin lógica

## Reglas
- No usar JavaScript salvo que sea estrictamente necesario; preferir C# e interactividad de Blazor
- Nunca escribir credenciales o API keys directo en el código; siempre via appsettings
- Seguir el modelo de datos: Users, Messages, MailboxEntries (una fila por destinatario)
- Mantener el estilo de código existente en el proyecto antes de proponer refactors grandes