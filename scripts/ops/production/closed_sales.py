"""Keep new orders and renewal resumption closed when legacy code is restored."""

MAP = b'''    # Retained rollback safety: historical payment callbacks remain available.
    map "$request_method:$uri" $maintenance_sales_closed {
        default 0;
        ~*^POST:/api/access/orders/?$ 1;
        ~*^POST:/api/access/me/grants/[0-9a-f-]+/resume-renewal/?$ 1;
    }
'''
GUARD = b'        if ($maintenance_sales_closed) { return 410; }\n'


def protect_nginx(content):
    if content.count(MAP) == 1 and content.count(GUARD) == content.count(b"    server {\n") > 0:
        return content
    if b"maintenance_sales_closed" in content or content.count(b"http {\n") != 1 or not content.count(b"    server {\n"):
        raise ValueError("unrecognized ingress configuration for closed sales")
    return content.replace(b"http {\n", b"http {\n" + MAP).replace(b"    server {\n", b"    server {\n" + GUARD)
