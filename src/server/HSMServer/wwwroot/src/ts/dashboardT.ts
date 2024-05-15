import {IPanel, ISourceUpdate} from "./dashboard.interfaces";
import {DataUpdate} from "./plotUpdate";

export namespace Dashboard {
    const updateDashboardInterval = 120000; // 2min
    
    export function initRequests(panel: IPanel[]) {
        for (let i in panel) {
            let update = new DataUpdate.Update(panel[i]);
            panel[i].requestTimeout = window.setInterval(function () {
                fetch(window.location.pathname + '/PanelUpdate' + `/${i}`, {
                    method: 'GET'
                }).then(res => res.json())
                    .then((res: ISourceUpdate[]) => {
                        update.updateSources(res)
                    })
            }, updateDashboardInterval)
        }
    }
}