import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig({
  plugins: [vue()],
  server: {
    proxy: {
      //Reenvía las llamadas de datos
      "/api": "http://localhost:5034",

      //Reenvía las imágenes subidas a la carpeta uploads
      "/uploads": "http://localhost:5034",
    },
  },
});
