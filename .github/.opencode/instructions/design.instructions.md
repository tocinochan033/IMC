# Guía de Estilos y Contexto UI - Proyecto Micelio Mail (.NET Razor Pages)

Este archivo define las reglas de diseño, paleta de colores, tipografía y estructura visual para la aplicación web de gestión de correo **Micelio**. Cuando generes vistas `.cshtml`, código de componentes o CSS, debes adherirte estrictamente a estas directrices.

## 1. Paleta de Colores (Design Tokens)

Aplica estos colores utilizando variables CSS (`var(--color-*)`) o clases utilitarias de Tailwind/Bootstrap configuradas según esta paleta.

| Rol del Color | Código HEX | Descripción / Uso | 
| ----- | ----- | ----- | 
| **Primary / Azul Botones** | `#2C5F8A` | Botones principales, elementos activos de menú, encabezados secundarios. | 
| **Primary Dark / Hover** | `#26323C` | Texto oscuro de encabezados, estado hover de botones principales, bordes oscuros. | 
| **Secondary Light / Selección** | `#EAF1F7` | Fondo del ítem seleccionado en listas, badges suaves, hover en listas. | 
| **Active Tint / Soft Hover** | `#EEF4F8` | Fondo de elementos con foco suave, estados alternativos. | 
| **Destructive / Red** | `#A33A3A` | Botones de descarte, acciones de eliminación o peligro. | 
| **Background Main** | `#F7F8F9` | Fondo general de la aplicación y contenedores secundarios. | 
| **Background Soft** | `#F3F5F7` | Fondo de la vista de inicio de sesión y paneles laterales suaves. | 
| **Surface White** | `#FFFFFF` | Tarjetas, contenedor principal de login, cuerpo de correos, inputs. | 
| **Border Soft** | `#D5DBE0` | Bordes de tarjetas, inputs, divisorias de listas. | 
| **Border Neutral** | `#B9C2C9` | Bordes de contenedores secundarios e inputs al interactuar. | 
| **Text Dark / Main** | `#26323C` | Texto principal, títulos de correo, nombres de remitentes. | 
| **Text Muted / Secondary** | `#68737D` | Texto secundario, fecha, hora, descripciones, previsualizaciones. | 
| **Text Light Muted** | `#8A949D` | Placeholder de inputs, contadores secundarios, texto deshabilitado. | 

## 2. Tipografía e Iconografía

* **Familia Tipográfica:** Usar font-family sans-serif limpia (preferiblemente `Inter`, `system-ui`, o `Segoe UI`).

* **Jerarquía de Texto:**

    * **H1 / Títulos principales:** `1.25rem` (20px) a `1.5rem` (24px), weight `600` (Semi-bold), color `#26323C`.

    * **Body / Lectura principal:** `0.875rem` (14px), weight `400` (Regular), color `#26323C`.

    * **Small / Metadata (fechas, remitente secondary):** `0.75rem` (12px), weight `400` / `500`, color `#68737D`.

* **Iconos:** Utilizar iconos lineales mínimos y limpios (Lucide Icons, FontAwesome Light/Regular o Bootstrap Icons).

## 3. Componentes Visuales y Vistas Principales

### A. Pantalla de Acceso (Login)

* **Estructura:** Tarjeta centrada de fondo blanco (`#FFFFFF`) con borde fino (`#D5DBE0`) sobre fondo tenue (`#F3F5F7`).

* **Campos de formulario:** Inputs con borde suave (`#D5DBE0`), bordes redondeados (`border-radius: 6px`), icono izquierdo integrado.

* **Botón Acción Principal:** Background `#2C5F8A`, texto `#FFFFFF`, ancho completo, bordes ligeramente redondeados (`4px` a `6px`).

* **Acciones secundarias:** Enlaces al pie en tono azul `#2C5F8A`.

### B. Layout de Aplicación Mail (Bandeja de Entrada / Redacción)

1. **Header Superior:**

    * Logo/Nombre de app a la izquierda (`Micelio`).

    * Barra de búsqueda central limpia con bordes suaves `#D5DBE0`, atajo de teclado visible (`Ctrl K`).

    * Perfil e íconos de configuración a la derecha.

2. **Sidebar Izquierda (Navegación):**

    * Botón superior destacado: **"Nuevo mensaje" / "Redactar"** (Fondo `#2C5F8A` o `#26323C`, texto blanco).

    * Menú principal de navegación (Bandeja de entrada, Destacados, Pospuestos, Enviados, Borradores, Archivo, Papelera).

    * Sección inferior con **Etiquetas** con contadores numéricos alineados a la derecha.

    * Ítem activo resaltado con fondo `#EAF1F7` y texto en `#2C5F8A`.

3. **Panel Intermedio (Lista de Mensajes / Borradores):**

    * Ancho fijo o proporcional (`300px` - `380px`).

    * Cada fila/tarjeta de correo separada por borde `#D5DBE0`.

    * Ítem seleccionado resalta con fondo `#EAF1F7` o `#EEF4F8` e indicador lateral izquierdo.

4. **Panel Derecho (Detalle del Correo / Editor):**

    * Vista previa del correo con avatar del usuario, remitente, destinatarios y archivos adjuntos (tarjetas en grid).

    * Para redacción: barra de herramientas de formato rich-text con íconos limpios y acciones inferiores (Enviar: `#2C5F8A`, Descartar: `#A33A3A`).