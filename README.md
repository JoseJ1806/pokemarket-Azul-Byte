# PokéMarket

Plataforma de subastas, valoración y mercado de cartas Pokémon de colección, diseñada para el mercado hispanohablante (Costa Rica y Panamá como punto de partida).

## ¿Qué problema resuelve?

El valor de una carta Pokémon no depende solo del personaje: depende de su **set, número, edición de impresión, idioma, acabado y grado de conservación** (PSA / BGS). Dos cartas con el mismo nombre pueden diferir en valor hasta 200 veces.

Hoy la empresa opera con una hoja de cálculo actualizada a mano y los coleccionistas de la región transan por WhatsApp, sin historial de precios ni garantías. PokéMarket reemplaza ese proceso con una plataforma profesional donde la identidad precisa de cada carta es la base del sistema.

## ¿Qué ofrece?

| Módulo | Descripción |
|---|---|
| **Catálogo y precios** | Taxonomía precisa de cartas, precios por grado de conservación e historial de cada cambio de precio. |
| **API de precios B2B** | API documentada y versionada, con SLA, para que tiendas y servicios integren los precios en sus sistemas. |
| **Subastas** | Subastas con integridad de pujas garantizada, cierre automático e historial público verificable. |
| **Inteligencia de mercado** | Market Index por set, Price Alerts configurables y Watchlist personal (modelo freemium). |

## Usuarios

- **Bidder:** coleccionista que busca cartas y participa en subastas.
- **AuctionManager:** publica y gestiona subastas (no puede pujar).
- **PriceEditor:** mantiene el catálogo y los precios, incluida la carga masiva por CSV.
- **SuperAdmin:** administra usuarios, configuración y exportaciones.
- **Clientes B2B:** dealers, tiendas y servicios como PokéGrading, que consumen la API.

## Fases del proyecto

1. **Fase 1 — Catálogo y API formal:** migrar a los 15 clientes B2B actuales a la nueva plataforma.
2. **Fase 2 — Subastas y mercado B2C:** lanzar el módulo de subastas.
3. **Fase 3 — Inteligencia y escala:** Market Index, Price Alerts y suscripción premium.

## Restricciones del MVP

- Sin procesamiento de pagos reales: las subastas se registran, pero el cobro ocurre fuera de la plataforma.
- Presupuesto de infraestructura menor o igual a $250 USD por mes.
- Desarrollo con Scrum como marco de trabajo.

## Documentación

- Caso de Negocio (v1.9)
- Documento de Requerimientos (v7)
- Descripción de Arquitectura
- Architecture Decision Log (ADR)
- Plan de Pruebas y Runbooks de Operación

La documentación técnica del equipo se mantiene en la wiki del proyecto.

## Equipo — Azul Byte

Gonzalo Acuña Madrigal
Daniel Ureña López 
Jose Julian Solano Quesada 
Sebastián Chaves Ruiz 


Proyecto del curso **CE-1116 Diseño y Calidad de Productos Tecnológicos**, Tecnológico de Costa Rica, II Semestre 2026.
