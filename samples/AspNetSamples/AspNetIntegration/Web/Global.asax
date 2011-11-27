<%@ Application Inherits="FSharpMVC3.Core.Global" Language="C#" %>
<script Language="C#" RunAt="server">

  protected void Application_Start(Object sender, EventArgs e) {
    // Delegate event handling to the F# Application class
    base.Start();
  }

</script>