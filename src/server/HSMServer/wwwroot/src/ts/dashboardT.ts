import {Panel, SourceUpdate} from "./dashboard.interfaces";
import {DataUpdate} from "./plotUpdate";

export namespace Dashboard {
    const updateDashboardInterval = 120000; // 2min
    const maxPlottedPoints = 1500;

    
    export function initRequests(panel: Panel[]) {
        for (let i in panel) {
            let update = new DataUpdate.Update(panel[i]);
            panel[i].requestTimeout = window.setInterval(function () {
                fetch(window.location.pathname + '/PanelUpdate' + `/${i}`, {
                    method: 'GET'
                }).then(res => res.json())
                    .then((res: SourceUpdate[]) => {
                        update.updateSources(res)
                    })
            }, updateDashboardInterval)
        }
    }
}