import { createSlice, configureStore } from '@reduxjs/toolkit'

export const TreeState = {
    Search: "search",
    Refreshing: "refreshing",
    RequestFinished: "requestFinished",
    Idle: "idle",
}

window.changeState = function (state){
    treeStoreState.dispatch(test(state))
}

export let refreshId = 0;
export let interval = 0;

const counterSlice = createSlice({
    name: 'counter',
    initialState: {
        state: TreeState.Idle,
    },
    reducers: {
        test: (state, action) => {
            state.state = action.payload;
        },
        refresh: (state, action) => {
            if (state.state === TreeState.Idle) {
                state.state = TreeState.Refreshing;
                $('#jstree').jstree(true).refresh(true, action.payload);
            }
            else {
                console.log(state);
            }
        },
        success: (state, action) => {
            // state.state = TreeState.Updating;
        },
        afterRefresh: (state, action) => {
            if (state.state === TreeState.Search) {
                return;
            }
            
            refreshId = setTimeout(() => {
                treeStoreState.dispatch(refresh())
            }, interval);
        },
        initialize: (state, action) => {
            interval = action.payload.interval;
            
            setTimeout(() => {
                treeStoreState.dispatch(refresh());
            }, interval)
        },
        search: (state, action) => {
            const t = setInterval(() => {
                $('#jstree').hide();
                if (treeStoreState.getState().state === TreeState.Idle) {
                    clearInterval(t);
                }
            }, 200)
            
            state.state = TreeState.Search;
            clearTimeout(refreshId);
        }
    }
})

export const {test, search: searchState, initialize, success, afterRefresh, refresh } = counterSlice.actions

export const treeStoreState = configureStore({
    reducer: counterSlice.reducer
})

// // Can still subscribe to the store
treeStoreState.subscribe(function () {
    const state = treeStoreState.getState().state;
    
    switch (state){
        case TreeState.Idle:
            refreshId = setTimeout(() => {
                treeStoreState.dispatch(refresh());
            }, interval)
            break;
    }
    
    console.log(state);
})
//
// // Still pass action objects to `dispatch`, but they're created for us
// store.dispatch(incremented())
// // {value: 1}
// store.dispatch(incremented())
// // {value: 2}
// store.dispatch(decremented())
// // {value: 1}