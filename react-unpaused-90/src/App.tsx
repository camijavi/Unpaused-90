import "./App.css";
import Dashboard from "./screens/Dashboard";
import { LanguageProvider } from "./languages/LanguageContext";

function App() {
  return (
    <LanguageProvider>
      <Dashboard />
    </LanguageProvider>
  );
}

export default App;
