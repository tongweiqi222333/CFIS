import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
export default defineConfig({plugins:[vue()],optimizeDeps:{include:["echarts/core","echarts/charts","echarts/components","echarts/renderers"]},build:{rollupOptions:{output:{manualChunks(id){if(id.includes("zrender"))return "chart-renderer";if(id.includes("echarts"))return "charts"}}}},server:{proxy:{'/api':(process.env.CFIS_API_TARGET ?? 'http://localhost:5080'),'/health':(process.env.CFIS_API_TARGET ?? 'http://localhost:5080')}}})
