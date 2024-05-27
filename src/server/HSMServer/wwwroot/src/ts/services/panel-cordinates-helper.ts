import {dashboardStorage} from "../../js/dashboard";

export class PanelCordinatesHelper{
 
    async recordinate(id: string){
        const panel = dashboardStorage.getPanel(id);
        const panelDiv =  document.getElementById(panel.id);
        const panelData = panelDiv.querySelector('.panel-data');
        console.log(panelDiv)
        console.log(panelDiv.getBoundingClientRect())
        console.log(panelDiv.getBoundingClientRect().width)
        
        console.log(panelData)
        console.log(panelData.getBoundingClientRect())
        console.log(panelData.getBoundingClientRect().width)
    }
}