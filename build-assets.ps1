cd ./src/designer/elsa-workflows-studio
npm install
npm run build

cd ../bindings/aspnet/Elsa.Designer.Components.Web
npm install
npm run build

cd ../../../../dashboards/blazor/ElsaDashboard.Application
npm install
npm run build