import { Hono } from "hono";

const app = new Hono();

app.notFound((context) => context.json({ error: "Not found" }, 404));

export default app;
