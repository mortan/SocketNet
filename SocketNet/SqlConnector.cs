using System.Configuration;

namespace SocketNet
{
    public class SqlConnector
    {
        public string ConnectionString
        {
            get
            {
                return ConfigurationManager.ConnectionStrings["DefaultConnectionString"].ConnectionString;
            }
        }

        public void Open()
        {
            
        }

        public void Close()
        {
            
        }

        public void ExecuteQuery()
        {
            
        }
    }
}
