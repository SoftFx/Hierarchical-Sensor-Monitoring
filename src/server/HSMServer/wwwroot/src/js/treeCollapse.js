window.collapseButton =  {
    treeState: undefined,
    isTreeCollapsed: undefined,
    
    tree: undefined,
    collapseIcon: undefined,
    
    isTriggered: false,
    
    collapse(){
        this.isTreeCollapsed = true;
        this.treeState = this.tree.jstree('get_state');
        this.isTriggered = true;

        $.ajax({
            type: 'put',
            url: `${closeNode}?nodeIds=${this.treeState.core.open}`,
            cache: false
        }).done(function (){
            collapseButton.tree.jstree('close_all');
            collapseButton.isTriggered = false;
        })
        
        this.collapseIcon.removeClass('fa-regular fa-square-minus').addClass('fa-regular fa-square-plus').attr('title', 'Restore tree')
    },
    
    restore(){
        this.isTreeCollapsed = false;
        this.tree.jstree('set_state', this.treeState);
        
        this.collapseIcon.removeClass('fa-regular fa-square-plus').addClass('fa-regular fa-square-minus').attr('title', 'Save and close tree');
    },
    
    collapseOnClick(){
        if (this.collapseIcon === undefined || this.jstree === undefined)
           this.init()

        if (this.isTreeCollapsed)
            this.restore()
        else 
            this.collapse()
    },

    
    init(){
        this.tree = $('#jstree');
        this.collapseIcon = $('#collapseIcon');
    },
    
    reset() {
        if (this.isTreeCollapsed){
            this.isTreeCollapsed = false;
            this.collapseIcon = $('#collapseIcon');
            this.collapseIcon.removeClass('fa-regular fa-square-plus').addClass('fa-regular fa-square-minus').attr('title', 'Save and close tree');
        }
    }
}