module Models.Api

// The identity layer exposes no reflected HTTP endpoints — auth rides the framework's /api/auth/*
// routes plus the composed IdentityHttp module. Content endpoints come from the composed `calendar`
// module (Calendar.Api).
